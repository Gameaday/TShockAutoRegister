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
    public override Version Version => new Version(2, 0, 0);
    public override string Author => "brian91292, moisterrific & HistoryLabs";

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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposalTokenSource.Cancel(); // Stop all pending GreetPlayer tasks
            _disposalTokenSource.Dispose();
            
            ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
            ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
        }
        base.Dispose(disposing);
    }

    private void OnServerJoin(JoinEventArgs args)
    {
        var tsSettings = TShock.Config.Settings;

        if (tsSettings.DisableUUIDLogin && !tsSettings.DisableLoginBeforeJoin) return;
        if (!tsSettings.RequireLogin && !Main.ServerSideCharacter) return;

        var player = TShock.Players[args.Who];
        if (player?.UUID == null) return;

        // Optimized Lookup: Search by UUID directly in DB, don't pull full list into memory
        var account = TShock.UserAccounts.GetUserAccountByName(player.Name) 
                      ?? TShock.UserAccounts.GetUserAccountByUUID(player.UUID);

        if (account == null && player.Name != TSServerPlayer.AccountName)
        {
            string rawPassword = GenerateSecurePassword(_config.PasswordLength);
            _pendingPasswords[player.UUID] = rawPassword;

            // Use TShock's internal manager to handle the addition logic
            TShock.UserAccounts.AddUserAccount(new UserAccount(
                player.Name,
                TShock.UserAccounts.CreateBCryptHash(rawPassword), // Modern hash method
                player.UUID,
                tsSettings.DefaultRegistrationGroupName,
                DateTime.UtcNow.ToString("s"),
                DateTime.UtcNow.ToString("s"),
                string.Empty
            ));

            TShock.Log.ConsoleInfo($"[AutoRegister] Auto-registered \"{player.Name}\"");
        }
    }

    private void OnGreetPlayer(GreetPlayerEventArgs args)
    {
        // Offload the delay to a Task that respects the plugin lifecycle
        _ = SendGreetingAsync(args.Who, _disposalTokenSource.Token);
    }

    private async Task SendGreetingAsync(int who, CancellationToken ct)
    {
        try 
        {
            await Task.Delay(1500, ct); // Respects Dispose() call

            var player = TShock.Players[who];
            if (player == null || ct.IsCancellationRequested) return;

            if (_pendingPasswords.TryRemove(player.UUID, out var password))
            {
                const string accent = "ff6347";
                string cmd = TShock.Config.Settings.CommandSpecifier;

                player.SendMessage($"[c/{accent}:[Auto-Reg]] Account created for \"{player.Name}\".", Color.White);
                player.SendMessage($"[c/{accent}:[Auto-Reg]] Temp Password: [c/ffffff:{password}]", Color.White);
                player.SendMessage($"[c/{accent}:[Auto-Reg]] Change via {cmd}password.", Color.White);
            }
        }
        catch (TaskCanceledException) { /* Clean exit on plugin reload */ }
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
