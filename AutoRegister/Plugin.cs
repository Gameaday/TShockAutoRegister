using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

// THE FIX: Alias the specific class to bypass the collision
using BC = BCrypt.Net.BCrypt;

#nullable enable

namespace AutoRegister;

[ApiVersion(2, 1)]
public class AutoRegister : TerrariaPlugin
{
    public override string Name => "AutoRegister";
    public override Version Version => new Version(2, 2, 3); 
    public override string Author => "HistoryLabs";

    private readonly ConcurrentDictionary<string, string> _pendingPasswords = new();
    private static readonly char[] PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();
    
    private Config _config = new();
    private CancellationTokenSource _disposalTokenSource = new();

    public AutoRegister(Main game) : base(game) { }

    public override void Initialize()
    {
        _config = Config.Read();
        ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
        GeneralHooks.ReloadEvent += OnReload;
        Commands.ChatCommands.Add(new Command("autoregister.admin", ReloadCommand, "autoreg"));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposalTokenSource.Cancel();
            _disposalTokenSource.Dispose();
            ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
            ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
            GeneralHooks.ReloadEvent -= OnReload;
        }
        base.Dispose(disposing);
    }

    private void OnServerJoin(JoinEventArgs args)
    {
        var player = TShock.Players[args.Who];
        if (player == null || string.IsNullOrEmpty(player.UUID) || player.Name == TSServerPlayer.AccountName) 
            return;

        var tsSettings = TShock.Config.Settings;
        if (!tsSettings.RequireLogin && !Main.ServerSideCharacter) return;

        var account = TShock.UserAccounts.GetUserAccountByName(player.Name);

        if (account == null)
        {
            string rawPassword = GenerateSecurePassword(_config.PasswordLength);
            
            // Use our alias 'BC' here. This avoids the OTAPI collision.
            string hashedPassword = BC.HashPassword(rawPassword);

            // TShock 6.1.0 UserAccount Constructor (7 arguments)
            var newAccount = new UserAccount(
                player.Name,                                // Name
                hashedPassword,                             // Password
                player.UUID,                                // UUID
                tsSettings.DefaultRegistrationGroupName,    // Group
                DateTime.UtcNow.ToString("s"),              // Registered
                DateTime.UtcNow.ToString("s"),              // LastAccessed
                ""                                          // Suffix (Email/Notes)
            );

            TShock.UserAccounts.AddUserAccount(newAccount);
            _pendingPasswords[player.UUID] = rawPassword;
            player.Account = newAccount;
            
            TShock.Log.ConsoleInfo($"[AutoRegister] Created and authenticated \"{player.Name}\".");
        }
    }

    private void OnGreetPlayer(GreetPlayerEventArgs args)
    {
        var player = TShock.Players[args.Who];
        if (player != null && !string.IsNullOrEmpty(player.UUID) && _pendingPasswords.ContainsKey(player.UUID))
        {
            _ = SendGreetingAsync(args.Who, player.UUID, _disposalTokenSource.Token);
        }
    }

    private async Task SendGreetingAsync(int who, string expectedUuid, CancellationToken ct)
    {
        try 
        {
            await Task.Delay(2000, ct);
            var player = TShock.Players[who];
            if (player == null || player.UUID != expectedUuid || ct.IsCancellationRequested) return;

            if (_pendingPasswords.TryRemove(player.UUID, out var password))
            {
                const string accent = "ff6347";
                string cmd = TShock.Config.Settings.CommandSpecifier;

                player.SendMessage($"[c/{accent}:[Auto-Reg]] Success! Your account is registered and logged in.", Color.White);
                player.SendMessage($"[c/{accent}:[Auto-Reg]] Temporary Password: [c/ffffff:{password}]", Color.White);
                player.SendMessage($"[c/{accent}:[Auto-Reg]] Use {cmd}password <old> <new> to change it.", Color.White);
            }
        }
        catch (TaskCanceledException) { }
    }

    private void OnReload(ReloadEventArgs args)
    {
        _config = Config.Read();
        args.Player?.SendSuccessMessage("[AutoRegister] Config reloaded.");
    }

    private void ReloadCommand(CommandArgs args) => OnReload(new ReloadEventArgs(args.Player));

    private static string GenerateSecurePassword(int length)
    {
        return string.Create(length, PasswordAlphabet, (chars, state) =>
        {
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = state[RandomNumberGenerator.GetInt32(state.Length)];
            }
        });
    }
}
