using System.Text.Json;
using System.Text.Json.Nodes;
using Eden.Mind;
using Eden.Mind.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Persists <see cref="BrainSettings"/> and today's spend in <c>user://</c>. The API key is encrypted with
/// AES-256-CBC (<see cref="AesContext"/>) under a random machine-local key file (<c>brain.key</c>, created on first
/// save with <see cref="Crypto.GenerateRandomBytes"/>); the settings file only holds base64(iv + ciphertext).
/// Not a vault: anyone with both files can decrypt. It keeps the key out of plain text and out of saves.
/// </summary>
public sealed class BrainSettingsStore(string directory = "user://")
{
    public string SettingsPath => directory + "brain_settings.json";
    public string KeyPath => directory + "brain.key";
    public string BudgetPath => directory + "brain_budget.json";

    public BrainSettings Load()
    {
        var text = ReadText(SettingsPath);
        if (text is null) return BrainDefaults.Settings;
        try
        {
            var o = JsonNode.Parse(text)!.AsObject();
            string provider = o["providerId"]?.GetValue<string>() ?? "worker";
            if (Array.IndexOf(BrainDefaults.ProviderIds, provider) < 0) provider = "worker";
            string? key = o["apiKey"]?.GetValue<string>() is { Length: > 0 } enc ? Decrypt(enc) : null;
            return new BrainSettings(provider,
                o["baseUrl"]?.GetValue<string>() ?? BrainDefaults.For(provider).BaseUrl,
                o["model"]?.GetValue<string>() ?? "",
                key,
                Math.Clamp(o["maxThinkingNpcs"]?.GetValue<int>() ?? 15, 0, 200),
                Math.Max(0, o["dailyNeuronBudget"]?.GetValue<double>() ?? 9000),
                Math.Clamp(o["concurrency"]?.GetValue<int>() ?? 3, 1, 16));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            GD.Print($"[brain] settings unreadable, using defaults: {e.Message}");
            return BrainDefaults.Settings;
        }
    }

    public void Save(BrainSettings s)
    {
        EnsureDir();
        var o = new JsonObject
        {
            ["providerId"] = s.ProviderId,
            ["baseUrl"] = s.BaseUrl,
            ["model"] = s.Model,
            ["apiKey"] = string.IsNullOrEmpty(s.ApiKey) ? "" : Encrypt(s.ApiKey),
            ["maxThinkingNpcs"] = s.MaxThinkingNpcs,
            ["dailyNeuronBudget"] = s.DailyNeuronBudget,
            ["concurrency"] = s.Concurrency,
        };
        WriteText(SettingsPath, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public void SaveBudget(BudgetLedger ledger)
    {
        var (day, neurons, usd) = ledger.Snapshot();
        EnsureDir();
        WriteText(BudgetPath, new JsonObject { ["day"] = day.ToString("yyyy-MM-dd"), ["neurons"] = neurons, ["usd"] = usd }.ToJsonString());
    }

    public void RestoreBudget(BudgetLedger ledger)
    {
        var text = ReadText(BudgetPath);
        if (text is null) return;
        try
        {
            var o = JsonNode.Parse(text)!;
            if (DateOnly.TryParse(o["day"]?.GetValue<string>(), out var day))
                ledger.Restore(day, o["neurons"]?.GetValue<double>() ?? 0, o["usd"]?.GetValue<double>() ?? 0);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { }
    }

    // ---- encryption ----------------------------------------------------------------------------

    public string Encrypt(string plain)
    {
        var data = System.Text.Encoding.UTF8.GetBytes(plain);
        int pad = 16 - data.Length % 16; // PKCS#7: AesContext wants whole blocks
        var padded = new byte[data.Length + pad];
        data.CopyTo(padded, 0);
        for (int i = data.Length; i < padded.Length; i++) padded[i] = (byte)pad;
        var iv = new Crypto().GenerateRandomBytes(16);
        var aes = new AesContext();
        aes.Start(AesContext.Mode.CbcEncrypt, MachineKey(), iv);
        var cipher = aes.Update(padded);
        aes.Finish();
        return Convert.ToBase64String([.. iv, .. cipher]);
    }

    public string? Decrypt(string encoded)
    {
        byte[] all;
        try { all = Convert.FromBase64String(encoded); } catch (FormatException) { return null; }
        if (all.Length < 32 || all.Length % 16 != 0 || !Godot.FileAccess.FileExists(KeyPath)) return null;
        var aes = new AesContext();
        aes.Start(AesContext.Mode.CbcDecrypt, MachineKey(), all[..16]);
        var plain = aes.Update(all[16..]);
        aes.Finish();
        int pad = plain.Length > 0 ? plain[^1] : 0;
        if (pad is < 1 or > 16 || pad > plain.Length) return null; // wrong machine key
        for (int i = plain.Length - pad; i < plain.Length; i++) if (plain[i] != pad) return null;
        return System.Text.Encoding.UTF8.GetString(plain, 0, plain.Length - pad);
    }

    private byte[] MachineKey()
    {
        if (Godot.FileAccess.FileExists(KeyPath))
        {
            var existing = Godot.FileAccess.GetFileAsBytes(KeyPath);
            if (existing.Length == 32) return existing;
        }
        EnsureDir();
        var key = new Crypto().GenerateRandomBytes(32);
        using var f = Godot.FileAccess.Open(KeyPath, Godot.FileAccess.ModeFlags.Write);
        f?.StoreBuffer(key);
        return key;
    }

    private void EnsureDir()
    {
        if (directory is "user://" or "res://") return;
        DirAccess.MakeDirRecursiveAbsolute(directory);
    }

    private static string? ReadText(string path)
        => Godot.FileAccess.FileExists(path) ? Godot.FileAccess.GetFileAsString(path) : null;

    private static void WriteText(string path, string text)
    {
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(text);
    }
}
