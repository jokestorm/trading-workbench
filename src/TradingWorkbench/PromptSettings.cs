using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TradingWorkbench;

public record CustomPrompt(string Text,string BasedOnVersion,string Revision);
public record PromptEdit(string Text,string BasedOnVersion,string Revision);
public record PromptReset(string Revision);
public record PromptView(string Kind,string DefaultText,string DefaultVersion,string? CustomText,string? BasedOnVersion,string Revision,bool UpdateAvailable,string ActiveSource);

// App defaults live in code; only personal overrides live in persistent data.
public sealed class PromptSettings(string directory,Func<string,PromptDefinition>? defaults=null)
{
    private readonly object sync=new();
    private PromptDefinition Default(string kind)=> (defaults??AnalysisPrompts.Default)(kind);
    private string PathFor(string kind)=>Path.Combine(directory,kind+".json");
    private PromptView Read(string kind)
    {
        if(kind is not ("market" or "journal"))throw new FormatException("Choose the market or journal prompt.");
        var baseline=Default(kind);var path=PathFor(kind);
        var custom=File.Exists(path)?JsonSerializer.Deserialize<CustomPrompt>(File.ReadAllText(path),CodexAnalysisProvider.Json)??throw new FormatException("Saved prompt settings are unreadable."):null;
        return new(kind,baseline.Text,baseline.Version,custom?.Text,custom?.BasedOnVersion,custom?.Revision??"default:"+baseline.Version,custom is not null&&custom.BasedOnVersion!=baseline.Version,custom is null?"default":"custom");
    }
    public PromptView Get(string kind){lock(sync)return Read(kind);}
    public PromptView Save(string kind,PromptEdit edit)
    {
        lock(sync)
        {
            var view=Read(kind);CheckRevision(view,edit.Revision);
            if(string.IsNullOrWhiteSpace(edit.Text)||edit.Text.Length>24000)throw new FormatException("Enter a prompt of 1–24,000 characters.");
            if(string.IsNullOrWhiteSpace(edit.BasedOnVersion)||(edit.BasedOnVersion!=view.DefaultVersion&&edit.BasedOnVersion!=view.BasedOnVersion))throw new FormatException("The default prompt changed. Reload settings before saving.");
            var custom=new CustomPrompt(edit.Text,edit.BasedOnVersion,Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);var path=PathFor(kind);
            File.WriteAllText(path+".tmp",JsonSerializer.Serialize(custom,CodexAnalysisProvider.Json));File.Move(path+".tmp",path,true);
            return Read(kind);
        }
    }
    private static void CheckRevision(PromptView view,string revision){if(view.Revision!=revision)throw new FormatException("Prompt settings changed in another window. Reload before saving.");}
    public PromptView Reset(string kind,string revision)
    {
        lock(sync){var view=Read(kind);CheckRevision(view,revision);var path=PathFor(kind);if(File.Exists(path))File.Delete(path);return Read(kind);}
    }
    public PromptDefinition Resolve(string kind)
    {
        var view=Get(kind=="journal"?"journal":"market");
        return view.CustomText is null?new(view.Kind,view.DefaultText,view.DefaultVersion):new(view.Kind,view.CustomText,"custom-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(view.CustomText))).ToLowerInvariant()[..16],"custom",view.BasedOnVersion);
    }
    public static void Map(WebApplication app,PromptSettings prompts)
    {
        app.MapGet("/api/ai/prompts",()=>new[]{prompts.Get("market"),prompts.Get("journal")});
        app.MapPut("/api/ai/prompts/{kind}",(string kind,PromptEdit edit)=>{try{return Results.Ok(prompts.Save(kind,edit));}catch(FormatException e){return Results.BadRequest(new{message=e.Message});}});
        app.MapPost("/api/ai/prompts/{kind}/default",(string kind,PromptReset reset)=>{try{return Results.Ok(prompts.Reset(kind,reset.Revision));}catch(FormatException e){return Results.BadRequest(new{message=e.Message});}});
    }
}
