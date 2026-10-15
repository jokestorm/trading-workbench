using System.Text.Json;

namespace TradingWorkbench;

public static class CodexLoginChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception("FAIL "+message);Console.WriteLine("PASS "+message);}
    private sealed class FakeSession(CodexDeviceLogin? login=null):ICodexLoginSession
    {
        public bool Disposed {get;private set;}
        public TaskCompletionSource<CodexLoginCompletion> Result {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CodexDeviceLogin> Begin(CancellationToken token)=>Task.FromResult(login??new("fixture-login","https://auth.openai.com/codex/device","ABCD-1234"));
        public Task<CodexLoginCompletion> Completion(CancellationToken token)=>Result.Task.WaitAsync(token);
        public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
    }
    private static async Task Settled(CodexLoginService service)
    {
        for(var i=0;i<100&&service.State.State=="pending";i++)await Task.Delay(10);
        if(service.State.State=="pending")throw new Exception("Auth fixture did not settle.");
    }
    public static async Task Run()
    {
        var connected=false;var starts=0;var logouts=0;var fake=new FakeSession();var provider=new CodexAnalysisProvider();
        await using var service=new CodexLoginService(()=>{starts++;return fake;},()=>Task.FromResult(new CodexStatus(true,connected,"fixture")),()=>{connected=false;logouts++;return Task.CompletedTask;},provider.EnterOperation);
        var first=await service.Begin();
        Check(first.State=="pending"&&first.UserCode=="ABCD-1234"&&first.ExpiresAt>DateTimeOffset.UtcNow,"device login exposes only a bounded one-time code and OpenAI link");
        await service.Begin();Check(starts==1,"repeated sign-in requests reuse the pending session");
        var blocked=false;try{using var lease=provider.EnterOperation();}catch(FormatException){blocked=true;}
        Check(blocked,"analysis cannot overlap an unfinished login");
        connected=true;fake.Result.SetResult(new("fixture-login",true));await Settled(service);
        Check(service.State.State=="connected"&&service.State.UserCode is null&&service.State.VerificationUrl is null,"verified login clears device code from public state");
        var json=JsonSerializer.Serialize(service.State);Check(!json.Contains("accessToken")&&!json.Contains("refreshToken")&&!json.Contains("ABCD-1234"),"public login state contains no token or completed device code");
        await service.Logout();Check(logouts==1&&!connected&&service.State.State=="idle","logout clears connection state and uses Codex-managed sign-out");
        fake=new FakeSession();await service.Begin();await service.Cancel();
        Check(fake.Disposed&&service.State.State=="idle"&&service.State.UserCode is null,"cancel disposes the device poller and clears its code");
        using(var lease=provider.EnterOperation())
        {
            blocked=false;try{await service.Begin();}catch(FormatException){blocked=true;}
            Check(blocked,"sign-in cannot interrupt an active analysis");
        }
        fake=new FakeSession();await service.Begin();connected=false;fake.Result.SetResult(new("fixture-login",true));await Settled(service);
        Check(service.State.State=="failed"&&service.State.UserCode is null,"unverified stored credentials cannot report a successful login");
        foreach(var url in new[]{"https://other.example/codex/device","javascript:alert(1)","http://auth.openai.com/codex/device","https://auth.openai.com:444/codex/device","https://auth.openai.com/codex/device?token=fixture"})
            Check(!CodexLoginService.Valid(new("fixture",url,"ABCD-1234")),"device login rejects an untrusted verification URL");
        fake=new FakeSession(new("fixture-login","https://other.example/","ABCD-1234"));
        blocked=false;try{await service.Begin();}catch(FormatException){blocked=true;}
        Check(blocked&&service.State.VerificationUrl is null&&fake.Disposed,"unexpected login links fail closed before reaching the browser");
        var home=Path.Combine(Path.GetTempPath(),"codex-settings-check-"+Guid.NewGuid().ToString("N"));
        var isolated=new CodexAnalysisProvider("fixture-codex",home).Start("login","status");
        Check(isolated.Environment["CODEX_HOME"]==home&&isolated.WorkingDirectory==home&&isolated.ArgumentList.Contains("cli_auth_credentials_store=\"file\""),"Pi login and analysis share the configured persistent private home");
        Check(!isolated.Environment.ContainsKey("OPENAI_API_KEY")&&!isolated.Environment.ContainsKey("CODEX_API_KEY")&&!isolated.Environment.ContainsKey("CODEX_ACCESS_TOKEN")&&isolated.ArgumentList.Contains("forced_login_method=\"chatgpt\""),"Codex subprocesses require ChatGPT login and exclude alternate credential environment variables");
    }
    public static async Task Probe()
    {
        var home=Path.Combine(Path.GetTempPath(),"codex-probe-"+Guid.NewGuid().ToString("N"));
        var provider=new CodexAnalysisProvider(home:home);
        var status=await provider.Status();Check(status.Installed&&!status.SubscriptionLogin,"bundled CLI starts with an isolated unauthenticated home");
        await using var session=new CodexLoginSession(provider);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Check(!await session.Probe(timeout.Token),"bundled Codex app-server initializes and reads auth state without credentials or model usage");
    }
}
