using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mu.Neural;
namespace AmdNrAssistant;

public sealed class SmartRuleCache(string root,Func<byte[],bool,byte[]> protect)
{
    sealed record Envelope(string Owner,string Server,string GameId,NeuralRuleDocument Document);
    string CachePath(string owner,string server,string game)=>Path.Combine(root,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner+"\n"+server+"\n"+game)))+".bin");
    public NeuralRuleDocument? Read(string owner,string server,string game,DateTimeOffset now)
    {
        try
        {
            var path=CachePath(owner,server,game);Core.RejectLinks(path);
            if(!File.Exists(path)||new FileInfo(path).Length>2*1024*1024)return null;
            var bytes=protect(File.ReadAllBytes(path),false);
            var envelope=JsonSerializer.Deserialize<Envelope>(bytes,NeuralRules.Json);CryptographicOperations.ZeroMemory(bytes);
            if(envelope==null||envelope.Document==null||envelope.Owner!=owner||envelope.Server!=server||envelope.GameId!=game||envelope.Document.ExpiresUtc<=now)return null;
            NeuralRules.Validate(envelope.Document);return envelope.Document;
        }catch(Exception e)when(e is IOException or InvalidDataException or JsonException or CryptographicException or System.ComponentModel.Win32Exception){return null;}
    }
    public void Write(string owner,string server,string game,NeuralRuleDocument document,DateTimeOffset now)
    {
        NeuralRules.Validate(document);
        if(document.ExpiresUtc<=now||document.ExpiresUtc>now.AddDays(31))throw new InvalidDataException("规则过期或有效期超限。");
        var raw=JsonSerializer.SerializeToUtf8Bytes(new Envelope(owner,server,game,document),NeuralRules.Json);
        var encrypted=protect(raw,true);CryptographicOperations.ZeroMemory(raw);
        Core.RejectLinks(root);Directory.CreateDirectory(root);var path=CachePath(owner,server,game);Core.RejectLinks(path);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temp,encrypted);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public async Task<(NeuralRuleDocument Document,string Source)> FetchAsync(string owner,string server,string game,Func<Task<NeuralRuleDocument>> fetch,DateTimeOffset now)
    {
        try
        {
            var document=await fetch();NeuralRules.Validate(document);
            if(document.ExpiresUtc<=now)throw new InvalidDataException("远程规则已过期。");
            Write(owner,server,game,document,now);return(document,"remote");
        }
        catch(Exception e)when(e is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException)
        {
            var cached=Read(owner,server,game,now);
            return cached==null?(new(1,"unavailable",now.AddMinutes(5),[]),"unavailable"):(cached,"cache");
        }
    }
}
