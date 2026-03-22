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

namespace AutoRegister
{
    [ApiVersion(2, 1)]
    public class Plugin : TerrariaPlugin
    {
        public override string Name => "AutoRegister";
        public override Version Version => new Version(2, 0, 0);
        public override string Author => "HistoryLabs";
        public override string Description => "Automatically registers accounts for new players.";

        private readonly ConcurrentDictionary<string, string> _pendingPasswords = new ConcurrentDictionary<string, string>();
        private static readonly char[] PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

        public static Config Config = new Config();

        public Plugin(Main game) : base(game) { }

        public override void Initialize()
        {
            Config = Config.Read();
            ServerApi.Hooks.ServerJoin.Register(this, OnServerJoin);
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer, 420);
        }

        private void OnServerJoin(JoinEventArgs args)
        {
            var tsConfig = TShock.Config.Settings;
            var player = TShock.Players[args.Who];

            if (player == null || string.IsNullOrWhiteSpace(player.UUID)) return;
            if (!tsConfig.RequireLogin && !Main.ServerSideCharacter) return;

            var accountByName = TShock.UserAccounts.GetUserAccountByName(player.Name);
            // Using a simple list check for maximum compatibility across TShock versions
            var accountByUUID = TShock.UserAccounts.GetUserAccounts().FirstOrDefault(u => u?.UUID == player.UUID);

            if (accountByName == null && accountByUUID == null && player.Name != TSServerPlayer.AccountName)
            {
                string rawPassword = GenerateSecurePassword(Config.PasswordLength);
                _pendingPasswords[player.UUID] = rawPassword;

                var newAccount = new UserAccount(
                    player.Name,
                    string.Empty,
                    player.UUID,
                    tsConfig.DefaultRegistrationGroupName,
                    DateTime.UtcNow.ToString("s"),
                    DateTime.UtcNow.ToString("s"),
                    string.Empty
                );

                newAccount.CreateBCryptHash(rawPassword, tsConfig.BCryptWorkFactor);
                TShock.UserAccounts.AddUserAccount(newAccount);
                TShock.Log.ConsoleInfo($"[AutoRegister] Registered \"{player.Name}\" ({player.IP})");
            }
        }

        private async void OnGreetPlayer(GreetPlayerEventArgs args)
        {
            var player = TShock.Players[args.Who];
            if (player == null || string.IsNullOrWhiteSpace(player.UUID)) return;

            await Task.Delay(1500);

            if (_pendingPasswords.TryRemove(player.UUID, out string? password))
            {
                string accent = Config.ChatPrefixColor;
                string cmd = TShock.Config.Settings.CommandSpecifier;

                player.SendMessage($"[c/{accent}:[Auto-Reg]] Account \"{player.Name}\" created.", Color.White);
                player.SendMessage($"[c/{accent}:[Auto-Reg]] Temp Password: [c/ffffff:{password}]", Color.White);
                player.SendMessage($"[c/{accent}:[Auto-Reg]] Use {cmd}password to change it.", Color.White);
            }
        }

        public static string GenerateSecurePassword(int length)
        {
            var chars = new char[length];
            for (int i = 0; i < length; i++)
                chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
            return new string(chars);
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
    }
}