using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgendaReminder.Core;
using AgendaReminder.Core.Accounts;
using AgendaReminder.Core.Google;
using AgendaReminder.Core.Storage;

namespace AgendaReminder.App.Services;

/// <summary>Comptes Google connectés : ajout, reconnexion, suppression, compte affiché.</summary>
internal sealed class AccountManager : IDisposable
{
    private readonly AccountList _list;
    private readonly Dictionary<string, GoogleAccountClient> _clients = new();
    private readonly Dictionary<string, string> _errors = new();

    public event Action? Changed;

    public AccountManager()
    {
        _list = JsonFileStore.LoadOrCreate(AppPaths.Accounts, () => new AccountList(), writeIfMissing: false);

        // Ancienne version (un seul compte, lecture seule) : jeton devenu inutile.
        try
        {
            var legacy = Path.Combine(AppPaths.Root, "token");
            if (Directory.Exists(legacy)) Directory.Delete(legacy, recursive: true);
        }
        catch (Exception ex) { Log.Error("Nettoyage de l'ancien jeton", ex); }
    }

    public IReadOnlyList<AccountInfo> Accounts => _list.Accounts;

    public IEnumerable<AccountInfo> Enabled => _list.Accounts.Where(a => a.Enabled);

    /// <summary>Compte affiché dans la fenêtre (null = tous les comptes).</summary>
    public string? SelectedId
    {
        get => _list.SelectedId is { } id && _list.Accounts.Any(a => a.Id == id) ? id : null;
        set
        {
            _list.SelectedId = value;
            Save();
            Changed?.Invoke();
        }
    }

    /// <summary>Compte utilisé par défaut pour créer un élément.</summary>
    public AccountInfo? DefaultForCreation =>
        _list.Accounts.FirstOrDefault(a => a.Id == SelectedId) ?? _list.Accounts.FirstOrDefault();

    public string? ErrorFor(string accountId) => _errors.TryGetValue(accountId, out var e) ? e : null;

    public string EmailOf(string? accountId) =>
        _list.Accounts.FirstOrDefault(a => a.Id == accountId)?.Email ?? "";

    public void SetError(string accountId, string? error)
    {
        if (error is null) _errors.Remove(accountId);
        else _errors[accountId] = error;
        Changed?.Invoke();
    }

    /// <summary>Client prêt à l'emploi pour un compte (sans jamais ouvrir le navigateur).</summary>
    public async Task<GoogleAccountClient> GetClientAsync(string accountId, CancellationToken ct = default)
    {
        if (_clients.TryGetValue(accountId, out var client))
            return client;
        client = await GoogleAccountClient.OpenAsync(accountId, ct);
        _clients[accountId] = client;
        return client;
    }

    /// <summary>Ajoute un compte (ouvre le navigateur). Un compte déjà présent est simplement reconnecté.</summary>
    public async Task<AccountInfo> AddAsync(CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N")[..10];
        var (client, email) = await GoogleAccountClient.AddInteractiveAsync(id, ct);

        var existing = _list.Accounts.FirstOrDefault(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            // Même compte : on remplace l'ancien jeton par le nouveau, en gardant l'identifiant existant.
            client.Dispose();
            Forget(existing.Id);
            GoogleAccountClient.DeleteLocalToken(existing.Id);
            Directory.Move(AppPaths.TokenStoreFor(id), AppPaths.TokenStoreFor(existing.Id));
            _errors.Remove(existing.Id);
            Save();
            Changed?.Invoke();
            return existing;
        }

        var account = new AccountInfo { Id = id, Email = email, Enabled = true };
        _list.Accounts.Add(account);
        _clients[id] = client;
        Save();
        Changed?.Invoke();
        return account;
    }

    /// <summary>Reconnecte un compte (nouveau consentement, par ex. après un changement d'autorisations).</summary>
    public async Task ReconnectAsync(string accountId, CancellationToken ct)
    {
        Forget(accountId);
        GoogleAccountClient.DeleteLocalToken(accountId);
        var (client, email) = await GoogleAccountClient.AddInteractiveAsync(accountId, ct);
        _clients[accountId] = client;
        var account = _list.Accounts.First(a => a.Id == accountId);
        account.Email = email;
        _errors.Remove(accountId);
        Save();
        Changed?.Invoke();
    }

    public async Task RemoveAsync(string accountId, CancellationToken ct)
    {
        try
        {
            var client = await GetClientAsync(accountId, ct);
            await client.RevokeAsync(ct);
        }
        catch (Exception ex)
        {
            Log.Error("Révocation du compte", ex);
        }
        Forget(accountId);
        GoogleAccountClient.DeleteLocalToken(accountId);
        _list.Accounts.RemoveAll(a => a.Id == accountId);
        _errors.Remove(accountId);
        Save();
        Changed?.Invoke();
    }

    public void SetEnabled(string accountId, bool enabled)
    {
        var account = _list.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null || account.Enabled == enabled) return;
        account.Enabled = enabled;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Oublie le client en mémoire (il sera rouvert au prochain besoin).</summary>
    public void Forget(string accountId)
    {
        if (_clients.Remove(accountId, out var client))
            client.Dispose();
    }

    /// <summary>Importe le fichier client_secret.json choisi par l'utilisateur.</summary>
    public static void ImportClientSecret(string sourcePath)
    {
        var text = File.ReadAllText(sourcePath);
        using (var doc = System.Text.Json.JsonDocument.Parse(text))
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("installed", out _))
            {
                throw new InvalidDataException(root.TryGetProperty("web", out _)
                    ? "Ce fichier est pour une « Application Web ». Créez un client de type « Application de bureau »."
                    : "Ce fichier ne ressemble pas à un identifiant OAuth Google (client_secret.json).");
            }
        }
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.ClientSecret, text);
    }

    private void Save()
    {
        try { JsonFileStore.Save(AppPaths.Accounts, _list); }
        catch (Exception ex) { Log.Error("Sauvegarde des comptes", ex); }
    }

    public void Dispose()
    {
        foreach (var c in _clients.Values) c.Dispose();
        _clients.Clear();
    }
}
