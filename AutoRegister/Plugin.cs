using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;

#nullable enable

namespace AutoRegister;

[ApiVersion(2, 1)]
public class AutoRegister : TerrariaPlugin
{
    public override string Name => "AutoRegister";
    public override Version Version => new Version(2, 0, 0);
    public override string Author => "brian91292, moisterrific & HistoryLabs";
    public override string Description => "Automatically registers accounts for new players with History Labs branding.";

    private readonly ConcurrentDictionary<string, string> _pendingPasswords = new();
    private static readonly char[] PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray(); // Removed ambiguous chars (0, O, 1, I)

    public static Config Config = new();

    public AutoRegister(Main game) : base(game) { }

    public override void Initialize()
    {
        Config = Config.Read();

        ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer, 420);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServerApi.Hooks.ServerJoin.Deregister(this, OnServerJoin);
            ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
        }
        base.Dispose(disposing);
    }

    private void OnServerJoin(JoinEventArgs args)
    {
        var tsConfig = TShock.Config.Settings;

        // Logic guard for configurations where players can't see chat before logging in
        if (tsConfig.DisableUUIDLogin && !tsConfig.DisableLoginBeforeJoin)
        {
            TShock.Log.ConsoleError("[AutoRegister] Plugin will not function properly when DisableUUIDLogin is true AND DisableLoginBeforeJoin is false.");
            return;
        }

        if (!tsConfig.RequireLogin && !Main.ServerSideCharacter) return;

        var player = TShock.Players[args.Who];
        if (player == null || string.IsNullOrWhiteSpace(player.UUID)) return;

        // Check if account already exists by name or UUID
        var accountByName = TShock.UserAccounts.GetUserAccountByName(player.Name);
        var accountByUUID = TShock.UserAccounts.GetUserAccounts().FirstOrDefault(acc => acc.UUID == player.UUID);

        if (accountByName == null && accountByUUID == null && player.Name != TSServerPlayer.AccountName)
        {
            string rawPassword = GenerateSecurePassword(Config.PasswordLength);
            _pendingPasswords[player.UUID] = rawPassword;

            var newAccount = new UserAccount(
                player.Name,
                string.Empty, // Password field set via CreateBCryptHash
                player.UUID,
                tsConfig.DefaultRegistrationGroupName,
                DateTime.UtcNow.ToString("s"),
                DateTime.UtcNow.ToString("s"),
                string.Empty
            );

            newAccount.CreateBCryptHash(rawPassword, tsConfig.BCryptWorkFactor);
            TShock.UserAccounts.AddUserAccount(newAccount);

            TShock.Log.ConsoleInfo($"[AutoRegister] Auto-registered \"{player.Name}\" ({player.IP})");
        }
    }

    private async void OnGreetPlayer(GreetPlayerEventArgs args)
    {
        var player = TShock.Players[args.Who];
        if (player == null || string.IsNullOrWhiteSpace(player.UUID)) return;

        // Give the player a second to finish loading in so they don't miss the message
        await Task.Delay(1500);

        if (_pendingPasswords.TryRemove(player.UUID, out string? password))
        {
            string accent = "ff6347"; // History Labs Tomato
            string cmd = TShock.Config.Settings.CommandSpecifier;

            player.SendMessage($"[c/{accent}:[Auto-Reg]] Your account \"{player.Name}\" has been created.", Color.White);
            player.SendMessage($"[c/{accent}:[Auto-Reg]] Temp Password: [c/ffffff:{password}]", Color.White);
            
            if (TShock.Config.Settings.DisableUUIDLogin)
            {
                player.SendMessage($"[c/{accent}:[Auto-Reg]] Use {cmd}login {password} to sign in.", Color.White);
            }

            player.SendMessage($"[c/{accent}:[Auto-Reg]] Change this using {cmd}password \"new_password\".", Color.White);
        }
        else if (!player.IsLoggedIn && TShock.Config.Settings.RequireLogin)
        {
            // If they aren't logged in and we didn't just register them, it means the name is taken
            var account = TShock.UserAccounts.GetUserAccountByName(player.Name);
            if (account != null && account.UUID != player.UUID)
            {
                player.SendErrorMessage("This name is already registered to another player.");
                player.SendErrorMessage("Please rejoin with a different name.");
            }
        }
    }

    private static string GenerateSecurePassword(int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        }
        return new string(chars);
    }
}