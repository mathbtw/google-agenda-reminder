namespace AgendaReminder.Core.Accounts;

public sealed class AccountInfo
{
    /// <summary>Identifiant local (nom du dossier de jeton).</summary>
    public string Id { get; set; } = "";

    public string Email { get; set; } = "";

    /// <summary>Les rappels de ce compte sont actifs.</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class AccountList
{
    public List<AccountInfo> Accounts { get; set; } = new();

    /// <summary>Compte affiché dans la fenêtre principale (null = tous).</summary>
    public string? SelectedId { get; set; }
}
