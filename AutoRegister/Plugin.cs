extern alias BCryptNet;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;
using BC = BCryptNet::BCrypt.Net.BCrypt;

#nullable enable

namespace AutoRegister;

[ApiVersion(2, 1)]
public class AutoRegister : TerrariaPlugin
{
    public override string Name => "AutoRegister Engine";
    public override Version Version => new Version(2, 5, 0); 
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
        
        // Respect TShock's native login requirements
        if (!tsSettings.RequireLogin && !Main.ServerSideCharacter) return;

        var account = TShock.UserAccounts.GetUserAccountByName(player.Name);

        if (account == null)
        {
            // Registration Phase: Frictionless generation
            string rawPassword = GenerateSecurePassword(_config.PasswordLength);
            string hashedPassword = BC.HashPassword(rawPassword);

            var newAccount = new UserAccount(
                player.Name,
                hashedPassword,
                player.UUID,
                tsSettings.DefaultRegistrationGroupName,
                DateTime.UtcNow.ToString("s"),
                DateTime.UtcNow.ToString("s"),
                string.Empty 
            );

            TShock.UserAccounts.AddUserAccount(newAccount);
            
            if (_config.ShowTemporaryPassword)
            {
                _pendingPasswords[player.UUID] = rawPassword;
            }
            
            player.Account = newAccount;
            TShock.Log.ConsoleInfo($"[AutoRegister] Created and authenticated \"{player.Name}\"");
        }
        else if (_config.EnableAutoLogin && !player.IsLoggedIn)
        {
            // Security Gate: IP Pinning + UUID Validation prevents hackers from spoofing a UUID
            bool isKnownIp = account.KnownIps != null && account.KnownIps.Contains($"\"{player.IP}\"");

            if (account.UUID == player.UUID)
            {
                if (isKnownIp || !_config.RequireStrictIPForAutoLogin)
                {
                    player.Account = account;
                    TShock.Log.ConsoleInfo($"[AutoRegister] Secure auto-login for \"{player.Name}\" (UUID + IP verified).");
                }
                else
                {
                    TShock.Log.ConsoleInfo($"[AutoRegister] BLOCKED auto-login for \"{player.Name}\". UUID matched, but IP ({player.IP}) is unknown.");
                }
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
        else if (player.IsLoggedIn && _config.EnableAutoLogin)
        {
            player.SendSuccessMessage("Welcome back! Your device was recognized and you have been securely logged in.");
        }
    }

    private async Task SendGreetingAsync(int who, string expectedUuid, CancellationToken ct)
    {
        try 
        {
            // Wait for world-load text to settle so the password isn't lost in chat spam
            await Task.Delay(2000, ct);

            var player = TShock.Players[who];
            if (player == null || player.UUID != expectedUuid || ct.IsCancellationRequested) return;

            if (_pendingPasswords.TryRemove(player.UUID, out var password))
            {
                const string accent = "B420F0"; 
                string cmd = TShock.Config.Settings.CommandSpecifier;

                player.SendMessage($"[c/{accent}:[Auto-Reg]] Success! Your account is registered and logged in.", Color.White);
                player.SendMessage($"[c/{accent}:[Auto-Reg]] Temporary Password: [c/ffffff:{password}]", Color.White);
                player.SendMessage($"[c/{accent}:[Auto-Reg]] Use {cmd}password <old> <new> to change it.", Color.White);
                
                if (_config.RequireStrictIPForAutoLogin)
                {
                    player.SendMessage($"[c/{accent}:[Auto-Reg]] Please save this password! If you change PCs, you will need it.", Color.White);
                }
            }
        }
        catch (TaskCanceledException) { }
    }

    private void OnReload(ReloadEventArgs args)
    {
        _config = Config.Read();
        args.Player?.SendSuccessMessage("[AutoRegister] Configuration reloaded.");
    }

    private void ReloadCommand(CommandArgs args)
    {
        if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "reload")
        {
            OnReload(new ReloadEventArgs(args.Player));
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
                chars[i] = state[System.Security.Cryptography.RandomNumberGenerator.GetInt32(state.Length)];
            }
        });
    }
}