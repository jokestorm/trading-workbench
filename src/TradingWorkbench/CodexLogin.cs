using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradingWorkbench;

public record CodexDeviceLogin(string LoginId,string VerificationUrl,string UserCode);
public record CodexLoginCompletion(string LoginId,bool Success);
public record CodexLoginState(string State,string Message,string? VerificationUrl=null,string? UserCode=null,DateTimeOffset? ExpiresAt=null);
public interface ICodexLoginSession:IAsyncDisposable
{
    Task<CodexDeviceLogin> Begin(CancellationToken token);
    Task<CodexLoginCompletion> Completion(CancellationToken token);
}

// Private stdio transport. Only device-auth calls are exposed, never general RPC,
// account tokens, subprocess output, or a network listener.
public sealed class CodexLoginSession:ICodexLoginSession
{
    private readonly Process process;
    private readonly CancellationTokenSource stop=new();
    private readonly ConcurrentDictionary<int,TaskCompletionSource<JsonElement>> pending=new();
    private readonly TaskCompletionSource<CodexLoginCompletion> completed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task reader,stderr;
    private int nextId;
    public CodexLoginSession(CodexAnalysisProvider provider)
    {
        process=new(){StartInfo=provider.Start("app-server","--listen","stdio://")};
        try{process.Start();}catch(System.ComponentModel.Win32Exception){process.Dispose();throw new FormatException("Codex could not start. Install the latest app update.");}
        stderr=process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        reader=Read();
    }
    private async Task Read()
    {
        try
        {
            while(await process.StandardOutput.ReadLineAsync(stop.Token) is {} line)
            {
                if(line.Length>1_000_000)throw new FormatException("Codex response was too large.");
                using var document=JsonDocument.Parse(line);var root=document.RootElement;
                if(root.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.Number&&pending.TryRemove(id.GetInt32(),out var waiter))
                {
                    if(root.TryGetProperty("error",out _))waiter.TrySetException(new FormatException("Codex could not complete device sign-in. Enable device-code login in your ChatGPT security settings and retry."));
                    else if(root.TryGetProperty("result",out var result))waiter.TrySetResult(result.Clone());
                    else waiter.TrySetException(new FormatException("Unexpected Codex response."));
                }
                else if(root.TryGetProperty("method",out var method)&&method.GetString()=="account/login/completed")
                {
                    var data=root.GetProperty("params");completed.TrySetResult(new(data.GetProperty("loginId").GetString()??"",data.GetProperty("success").GetBoolean()));
                }
            }
        }
        catch(Exception error) when(error is IOException or OperationCanceledException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException){/* Never expose auth-bearing output. */}
        finally
        {
            var failure=new FormatException("Codex sign-in connection ended. Try signing in again.");
            foreach(var waiter in pending.Values)waiter.TrySetException(failure);
            completed.TrySetException(failure);
        }
    }
    private async Task<JsonElement> Call(string method,object parameters,CancellationToken token)
    {
        var id=Interlocked.Increment(ref nextId);var waiter=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);pending[id]=waiter;
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{id,method,@params=parameters}));
            await process.StandardInput.FlushAsync(token);
            return await waiter.Task.WaitAsync(TimeSpan.FromSeconds(25),token);
        }
        finally{pending.TryRemove(id,out _);}
    }
    private async Task Initialize(CancellationToken token)
    {
        await Call("initialize",new{clientInfo=new{name="trading_workbench",title="Trading Workbench",version="0.1.2"}},token);
        await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}");await process.StandardInput.FlushAsync(token);
    }
    public async Task<bool> Probe(CancellationToken token)
    {
        await Initialize(token);
        var result=await Call("account/read",new{refreshToken=false},token);
        return result.TryGetProperty("account",out var account)&&account.ValueKind!=JsonValueKind.Null;
    }
    public async Task<CodexDeviceLogin> Begin(CancellationToken token)
    {
        await Initialize(token);
        var result=await Call("account/login/start",new{type="chatgptDeviceCode"},token);
        return new(result.GetProperty("loginId").GetString()!,result.GetProperty("verificationUrl").GetString()!,result.GetProperty("userCode").GetString()!);
    }
    public Task<CodexLoginCompletion> Completion(CancellationToken token)=>completed.Task.WaitAsync(token);
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}catch(Exception error) when(error is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception){}
        try{await Task.WhenAll(reader,stderr).WaitAsync(TimeSpan.FromSeconds(5));}catch(Exception error) when(error is IOException or TimeoutException or OperationCanceledException){}
        process.Dispose();stop.Dispose();
    }
}

public sealed class CodexLoginService(Func<ICodexLoginSession> create,Func<Task<CodexStatus>> status,Func<Task> logout,Func<IDisposable> enterOperation):IAsyncDisposable
{
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly object sync=new();
    private CodexLoginState state=new("idle","Sign in with ChatGPT to enable analysis.");
    private CancellationTokenSource? cancellation;
    private Task? running;
    public CodexLoginState State{get{lock(sync)return state;}}
    private void Set(CodexLoginState value){lock(sync)state=value;}
    public static bool Valid(CodexDeviceLogin login)=>!string.IsNullOrWhiteSpace(login.LoginId)
        &&Uri.TryCreate(login.VerificationUrl,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="auth.openai.com"&&uri.IsDefaultPort
        &&uri.AbsolutePath.TrimEnd('/')=="/codex/device"&&uri.Query.Length==0&&uri.Fragment.Length==0&&uri.UserInfo.Length==0
        &&Regex.IsMatch(login.UserCode??"","^[A-Z0-9-]{6,32}$",RegexOptions.CultureInvariant);
    public async Task<CodexLoginState> Begin(CancellationToken token=default)
    {
        await gate.WaitAsync(token);
        try
        {
            if(State.State=="pending")return State;
            if(running is not null)await running;
            cancellation?.Dispose();cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var lease=enterOperation();var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            running=Run(cancellation.Token,lease,ready);
            try{await ready.Task.WaitAsync(TimeSpan.FromSeconds(30),token);}
            catch{cancellation.Cancel();await running;throw;}
            return State;
        }
        finally{gate.Release();}
    }
    private async Task Run(CancellationToken token,IDisposable lease,TaskCompletionSource ready)
    {
        try
        {
            await using var session=create();
            var login=await session.Begin(token);
            if(!Valid(login))throw new FormatException("Codex returned an unexpected sign-in link or code. Update the app and retry.");
            Set(new("pending","Open the OpenAI sign-in page and enter this one-time code.",login.VerificationUrl,login.UserCode,DateTimeOffset.UtcNow.AddMinutes(15)));
            ready.TrySetResult();
            var result=await session.Completion(token);
            if(result.LoginId!=login.LoginId||!result.Success)throw new FormatException("Sign-in did not finish. Enable device-code login in your ChatGPT security settings and retry.");
            if(!(await status()).SubscriptionLogin)throw new FormatException("Could not verify the saved ChatGPT login. Try signing in again.");
            Set(new("connected","ChatGPT connected."));
        }
        catch(OperationCanceledException){Set(new("failed","Sign-in expired or was canceled. Start again when ready."));ready.TrySetCanceled();}
        catch(Exception error) when(error is FormatException or IOException or InvalidOperationException or JsonException or TimeoutException or KeyNotFoundException or System.ComponentModel.Win32Exception)
        {
            var message=error is FormatException?error.Message:"Codex could not start sign-in. Check the connection and try again.";
            Set(new("failed",message));ready.TrySetException(new FormatException(message));
        }
        finally{lease.Dispose();}
    }
    private async Task Stop()
    {
        cancellation?.Cancel();if(running is not null)await running;
        cancellation?.Dispose();cancellation=null;running=null;
    }
    public async Task<CodexLoginState> Cancel()
    {
        await gate.WaitAsync();try{await Stop();Set(new("idle","Sign-in canceled."));return State;}finally{gate.Release();}
    }
    public async Task<CodexLoginState> Logout()
    {
        await gate.WaitAsync();
        try{await Stop();using var lease=enterOperation();await logout();Set(new("idle","Signed out of ChatGPT."));return State;}
        finally{gate.Release();}
    }
    public async ValueTask DisposeAsync(){await gate.WaitAsync();try{await Stop();}finally{gate.Release();}}
    public static void Map(WebApplication app,CodexLoginService login)
    {
        app.MapGet("/api/ai/login",()=>login.State);
        app.MapPost("/api/ai/login/start",async(HttpContext context)=>{try{return Results.Ok(await login.Begin(context.RequestAborted));}catch(Exception e) when(e is FormatException or TimeoutException){return Results.BadRequest(new{message=e.Message});}});
        app.MapPost("/api/ai/login/cancel",async()=>Results.Ok(await login.Cancel()));
        app.MapPost("/api/ai/logout",async()=>{try{return Results.Ok(await login.Logout());}catch(Exception e) when(e is FormatException or OperationCanceledException){return Results.BadRequest(new{message="Could not sign out. Wait for any running analysis and try again."});}});
    }
}
