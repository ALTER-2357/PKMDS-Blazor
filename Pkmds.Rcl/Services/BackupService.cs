namespace Pkmds.Rcl.Services;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using PKHeX.Core;

/// <summary>
/// Simple IndexedDB-backed backup service for PKM entries.
/// Stores raw decrypted PKM bytes and metadata via JS interop (IndexedDB).
/// </summary>
public interface IBackupService
{
    Task<BackupEntry> SavePokemonBackupAsync(PKM pkm);
    Task<IEnumerable<BackupEntry>> ListBackupsAsync();
    Task<PKM?> GetBackupPokemonAsync(Guid id);
    Task DeleteBackupAsync(Guid id);
    Task<bool> RestoreBackupToSaveAsync(Guid id);
}

public record BackupEntry(Guid Id, string FileName, string SpeciesName, int Species, string Extension, long CreatedUtc, string Sha256);

public class BackupService : IBackupService
{
    private readonly IJSRuntime _js;
    private readonly IAppState _appState;
    private readonly IAppService _appService;

    public BackupService(IJSRuntime js, IAppState appState, IAppService appService)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
        _appState = appState ?? throw new ArgumentNullException(nameof(appState));
        _appService = appService ?? throw new ArgumentNullException(nameof(appService));
    }

    public async Task<BackupEntry> SavePokemonBackupAsync(PKM pkm)
    {
        if (pkm is null) throw new ArgumentNullException(nameof(pkm));

        pkm.RefreshChecksum();
        var data = pkm.DecryptedPartyData ?? Array.Empty<byte>();
        var sha = ComputeSha256Hex(data);
        var fileName = _appService.GetCleanFileName(pkm);
        var speciesName = _appService.GetPokemonSpeciesName(pkm.Species) ?? "Unknown";

        var meta = new
        {
            fileName,
            species = pkm.Species,
            speciesName,
            extension = pkm.Extension,
            createdUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            sha256 = sha
        };

        var id = Guid.NewGuid();
        // JS saves the bytes and metadata under the id
        await _js.InvokeVoidAsync("pkmdsBackup.saveBackup", id.ToString(), data, JsonSerializer.Serialize(meta));

        return new BackupEntry(id, fileName, speciesName, pkm.Species, pkm.Extension, meta.createdUtc, sha);
    }

    public async Task<IEnumerable<BackupEntry>> ListBackupsAsync()
    {
        var json = await _js.InvokeAsync<string>("pkmdsBackup.listBackupMeta");
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<BackupEntry>();

        var list = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json) ?? new();
        var res = new List<BackupEntry>(list.Count);
        foreach (var m in list)
        {
            if (!m.TryGetValue("id", out var idObj)) continue;
            var id = Guid.Parse(idObj.ToString()!);
            var fn = m.TryGetValue("fileName", out var f) ? f?.ToString() ?? "unknown" : "unknown";
            var speciesName = m.TryGetValue("speciesName", out var s) ? s?.ToString() ?? "" : "";
            var species = m.TryGetValue("species", out var sp) ? Convert.ToInt32(sp) : 0;
            var ext = m.TryGetValue("extension", out var e) ? e?.ToString() ?? ".pkm" : ".pkm";
            var created = m.TryGetValue("createdUtc", out var c) ? Convert.ToInt64(c) : 0L;
            var sha = m.TryGetValue("sha256", out var sh) ? sh?.ToString() ?? "" : "";
            res.Add(new BackupEntry(id, fn, speciesName, species, ext, created, sha));
        }
        return res;
    }

    public async Task<PKM?> GetBackupPokemonAsync(Guid id)
    {
        var data = await _js.InvokeAsync<byte[]>("pkmdsBackup.getBackupData", id.ToString());
        if (data == null || data.Length == 0) return null;

        // Use existing FileUtil.TryGetPKM to parse; provide extension ".pkm" so detection can proceed
        if (!FileUtil.TryGetPKM(data, out var pkm, ".pkm", _appState.SaveFile))
            return null;

        return pkm;
    }

    public Task DeleteBackupAsync(Guid id) =>
        _js.InvokeVoidAsync("pkmdsBackup.deleteBackup", id.ToString()).AsTask();

    public async Task<bool> RestoreBackupToSaveAsync(Guid id)
    {
        var pkm = await GetBackupPokemonAsync(id);
        if (pkm is null || _appState.SaveFile is null) return false;

        // Convert to save's PKM type if required
        if (pkm.GetType() != _appState.SaveFile.PKMType)
        {
            pkm = EntityConverter.ConvertToType(pkm, _appState.SaveFile.PKMType, out var c);
            if (!c.IsSuccess() || pkm is null) return false;
        }

        var index = _appState.SaveFile.NextOpenBoxSlot();
        if (index < 0) return false;

        _appState.SaveFile.SetBoxSlotAtIndex(pkm, index);
        return true;
    }

    private static string ComputeSha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.AppendFormat("{0:x2}", b);
        return sb.ToString();
    }
}
