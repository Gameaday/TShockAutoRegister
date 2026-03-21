using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

#nullable enable

namespace AutoRegister;

[ApiVersion(2, 1)]
public class AutoRegister : TerrariaPlugin
{
    public override string Name => "AutoRegister";
    public override Version Version => new Version(2, 2, 0); 
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
        var tsSettings = TShock.Config.Settings;
        var player = TShock.Players[args.Who];

        if (player == null || string.IsNullOrEmpty(player.UUID)) return;
        if (player.Name == TSServerPlayer.AccountName) return;

        // Logic check: Don't run if login isn't required by the server
        if (!tsSettings.RequireLogin && !Main.ServerSideCharacter) return;

        // Optimized Lookup: Check Name first, then UUID
        var account = TShock.UserAccounts.GetUserAccountByName(player.Name) 
                      ?? TShock.UserAccounts.GetUserAccountByUUID(player.UUID);

        if (account == null)
        {
            string rawPassword = GenerateSecurePassword(_config.PasswordLength);
            
            var newAccount = new UserAccount(
                player.Name,
                TShock.UserAccounts.CreateBCryptHash(rawPassword),
                player.UUID,
                tsSettings.DefaultRegistrationGroupName,
                DateTime.UtcNow.ToString("s"),
                DateTime.UtcNow.ToString("s"),
                string.Empty
            );

            if (TShock.UserAccounts.AddUserAccount(newAccount))
            {
                _pendingPasswords[player.UUID] = rawPassword;
                
                // CRITICAL IMPROVEMENT: Automatically log the player in
                // This prevents the "Please login" nag immediately.
                player.SetUserAccount(newAccount);
                
                TShock.Log.ConsoleInfo($"[AutoRegister] Created and logged in \"{player.Name}\" ({player.UUID})");
            }
        }
    }

    private void OnGreetPlayer(GreetPlayerEventArgs args)
    {
        var player = TShock.Players[args.Who];
        if (player == null || string.IsNullOrEmpty(player.UUID)) return;

        if (_pendingPasswords.ContainsKey(player.UUID))
        {
            _ = SendGreetingAsync(args.Who, player.UUID, _disposalTokenSource.Token);
        }
    }

    private async Task SendGreetingAsync(int who, string expectedUuid, CancellationToken ct)
    {
        try 
        {
            // Give the client a moment to finish loading the world
            await Task.Delay(2000, ct);

            var player = TShock.Players[who];
            if (player == null || player.UUID != expectedUuid || ct.IsCancellationRequested) return;

            if (_pendingPasswords.TryRemove(player.UUID, out var password))
            {
                const string accent = "ff6347"; // History Labs Tomato
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
        // Allocation-free string generation using RandomNumberGenerator
        return string.Create(length, PasswordAlphabet, (chars, state) =>
        {
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = state[RandomNumberGenerator.GetInt32(state.Length)];
            }
        });
    }
}
