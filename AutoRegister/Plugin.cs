using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using System.Security.Cryptography;

#nullable enable

namespace AutoRegister;

[ApiVersion(2, 1)]
public class AutoRegister : TerrariaPlugin
{
    public override string Name => "AutoRegister";
    public override Version Version => new Version(2, 1, 0); // Incremented for the reload update
    public override string Author => "HistoryLabs";

    private readonly ConcurrentDictionary<string, string> _pendingPasswords = new();
    private static readonly char[] PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();
    
    private Config _config = new();
    private readonly CancellationTokenSource _disposalTokenSource = new();

    public AutoRegister(Main game) : base(game) { }

    public override void Initialize()
    {
        _config = Config.Read();

        ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);

        // Standard command for config reloading
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
        }
        base.Dispose(disposing);
    }

    private void OnServerJoin(JoinEventArgs args)
    {
        var tsSettings = TShock.Config.Settings;

        // Logic check: Can we actually see the player yet?
        if (tsSettings.DisableUUIDLogin && !tsSettings.DisableLoginBeforeJoin) return;
        if (!tsSettings.RequireLogin && !Main.ServerSideCharacter) return;

        var player = TShock.Players[args.Who];
        if (player?.UUID == null) return;

        // Optimized Lookup
        var account = TShock.UserAccounts.GetUserAccountByName(player.Name) 
                      ?? TShock.UserAccounts.GetUserAccountByUUID(player.UUID);

        if (account == null && player.Name != TSServerPlayer.AccountName)
        {
            string rawPassword = GenerateSecurePassword(_config.PasswordLength);
            _pendingPasswords[player.UUID] = rawPassword;

            TShock.UserAccounts.AddUserAccount(new UserAccount(
                player.Name,
                TShock.UserAccounts.CreateBCryptHash(rawPassword),
                player.UUID,
                tsSettings.DefaultRegistrationGroupName,
                DateTime.UtcNow.ToString("s"),
                DateTime.UtcNow.ToString("s"),
                string.Empty
            ));

            TShock.Log.ConsoleInfo($"[AutoRegister] Account created for \"{player.Name}\" via UUID {player.UUID}");
        }
    }

    private void OnGreetPlayer(GreetPlayerEventArgs args)
    {
        var player = TShock.Players[args.Who];
        if (player == null || string.IsNullOrEmpty(player.UUID)) return;

        // Fire and forget, but with identity verification
        _ = SendGreetingAsync(args.Who, player.UUID, _disposalTokenSource.Token);
    }

    private async Task SendGreetingAsync(int who, string expectedUuid, CancellationToken ct)
    {
        try 
        {
            await Task.Delay(1500, ct);

            var player = TShock.Players[who];
            
            // CRITICAL: Ensure the player in this slot is still the one we registered
            if (player == null || player.UUID != expectedUuid || ct.IsCancellationRequested) return;

            if (_pendingPasswords.TryRemove(player.UUID, out var password))
            {
                const string accent = "ff6347"; // History Labs Branding
                string cmd = TShock.Config.Settings.CommandSpecifier;

                player.SendMessage($"[c/{accent}:[Auto-Reg]] Success! Account \"{player.Name}\" is ready.", Color.White);
                player.SendMessage($"[c/{accent}:[Auto-Reg]] Temp Password: [c/ffffff:{password}]", Color.White);
                player.SendMessage($"[c/{accent}:[Auto-Reg]] Update this immediately using {cmd}password.", Color.White);
            }
        }
        catch (TaskCanceledException) { /* Clean exit */ }
    }

    private void ReloadCommand(CommandArgs args)
    {
        if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "reload")
        {
            _config = Config.Read();
            args.Player.SendSuccessMessage("AutoRegister config reloaded!");
        }
        else
        {
            args.Player.SendErrorMessage("Usage: /autoreg reload");
        }
    }

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
