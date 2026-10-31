using System;
using System.Collections.Generic;
using System.Linq;
using Silverdash360.Core;
using Silverdash360.Model;
using Silverdash360.Native;

namespace Silverdash360.Services
{
    /// <summary>Signed-in profiles, their gamer cards, played titles and achievements.</summary>
    public sealed class ProfileService
    {
        private const uint GamercardZone = 0x10040004;
        private const uint GamercardCred = 0x10040006;
        private const uint GamercardTitlesPlayed = 0x10040012;
        private const uint GamercardAchievementsEarned = 0x10040013;
        private const uint GamercardMotto = 0x402C0011;

        private const int TitlePlayedSize = 0xA8;
        private const int AchievementSize = 36;
        private const int AchievementStrings = 464;
        private const uint AchievementDetailsAll = 0xFF;

        /// <summary>The profiles signed in now, by controller.</summary>
        public List<Gamer> SignedIn()
        {
            List<Gamer> gamers = new List<Gamer>();
            for (uint i = 0; i < Xam.MaxUsers; i++)
            {
                Gamer gamer = Read(i);
                if (gamer != null)
                {
                    gamers.Add(gamer);
                }
            }
            return gamers;
        }

        /// <summary>The profile signed in on a controller, or null.</summary>
        public Gamer Read(uint userIndex)
        {
            if (Xam.XamUserGetSigninState(userIndex) == 0)
            {
                return null;
            }
            Gamer gamer = new Gamer { UserIndex = userIndex };
            using (NativeBuffer info = new NativeBuffer(0x28))
            {
                if (Xam.XamUserGetSigninInfo(userIndex, 0, info.Address) != Xam.ErrorSuccess)
                {
                    return null;
                }
                gamer.Xuid = info.U64(0x00);
                gamer.State = (SigninState)info.U32(0x0C);
                gamer.Gamertag = info.Ansi(0x18, 16);
                gamer.OfflineXuid = Xam.XamUserGetSigninInfo(userIndex, Xam.SigninInfoOfflineXuidOnly, info.Address) ==
                                    Xam.ErrorSuccess ? info.U64(0x00) : gamer.Xuid;
            }
            ReadGamercard(gamer);
            return gamer;
        }

        private static void ReadGamercard(Gamer gamer)
        {
            uint[] ids = { GamercardCred, GamercardAchievementsEarned, GamercardTitlesPlayed, GamercardZone, GamercardMotto };
            using (NativeBuffer request = new NativeBuffer(ids.Length * 4 + 8))
            using (NativeBuffer results = new NativeBuffer(0x1000))
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    request.SetU32(i * 4, ids[i]);
                }
                int sizeAt = ids.Length * 4;
                request.SetU32(sizeAt, (uint)results.Length);
                uint result = Xam.XamUserReadProfileSettings(0, gamer.UserIndex, 0, 0, (uint)ids.Length,
                                                             request.Address, request[sizeAt], results.Address, 0);
                if (result != Xam.ErrorSuccess)
                {
                    Log.Write("Reading the gamer card of {0} failed: {1}", gamer.Gamertag, result);
                    return;
                }
                // XUSER_READ_PROFILE_SETTING_RESULT: a count, then a pointer to
                // 40-byte XUSER_PROFILE_SETTINGs in the same buffer.
                uint count = results.U32(0);
                uint settings = results.U32(4);
                if (!results.Contains(settings))
                {
                    return;
                }
                for (int i = 0; i < count; i++)
                {
                    int s = results.OffsetOf(settings) + i * 40;
                    if (s + 40 > results.Length)
                    {
                        break;
                    }
                    uint id = results.U32(s + 0x10);
                    byte type = results.U8(s + 0x18);
                    int value = (int)results.U32(s + 0x20);
                    switch (id)
                    {
                        case GamercardCred: gamer.Gamerscore = value; break;
                        case GamercardAchievementsEarned: gamer.AchievementsEarned = value; break;
                        case GamercardTitlesPlayed: gamer.TitlesPlayed = value; break;
                        case GamercardZone: gamer.Zone = value; break;
                        case GamercardMotto:
                            if (type == 4)
                            {
                                gamer.Motto = results.Utf16At(results.U32(s + 0x24)) ?? "";
                            }
                            break;
                    }
                }
            }
        }

        /// <summary>The titles the profile has played, most recent first.</summary>
        public List<PlayedTitle> PlayedTitles(Gamer gamer)
        {
            List<PlayedTitle> titles = new List<PlayedTitle>();
            const uint batch = 32;
            uint bufferSize, handle;
            uint result = Xam.XamUserCreateTitlesPlayedEnumerator(0, gamer.UserIndex, 0, 0, batch, out bufferSize, out handle);
            if (result != Xam.ErrorSuccess)
            {
                Log.Write("Listing the titles {0} played failed: {1}", gamer.Gamertag, result);
                return titles;
            }
            try
            {
                using (NativeBuffer buffer = new NativeBuffer((int)Math.Max(bufferSize, batch * TitlePlayedSize)))
                {
                    uint count;
                    while (Xam.XamEnumerate(handle, 0, buffer.Address, (uint)buffer.Length, out count, 0) == Xam.ErrorSuccess && count > 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int o = i * TitlePlayedSize;
                            titles.Add(new PlayedTitle
                            {
                                TitleId = buffer.U32(o + 0x00),
                                AchievementsTotal = (int)buffer.U32(o + 0x04),
                                AchievementsUnlocked = (int)buffer.U32(o + 0x08),
                                GamerscoreTotal = (int)buffer.U32(o + 0x0C),
                                GamerscoreEarned = (int)buffer.U32(o + 0x10),
                                LastPlayed = FileTime(buffer.U64(o + 0x20)),
                                Name = buffer.Utf16(o + 0x28, 64),
                            });
                        }
                    }
                }
            }
            finally
            {
                Kernel.NtClose(handle);
            }
            // Sorted by a key rather than a comparison: a comparison lambda is
            // interpreted app code called for every pair, a key lambda once a title.
            return titles.OrderByDescending(t => t.LastPlayed ?? DateTime.MinValue).ToList();
        }

        /// <summary>A title's achievements for the profile.</summary>
        public List<Achievement> Achievements(Gamer gamer, uint titleId)
        {
            List<Achievement> achievements = new List<Achievement>();
            const uint batch = 16;
            uint bufferSize, handle;
            uint result = Xam.XamUserCreateAchievementEnumerator(titleId, gamer.UserIndex, 0, AchievementDetailsAll, 0, batch,
                                                                 out bufferSize, out handle);
            if (result != Xam.ErrorSuccess)
            {
                Log.Write("Listing achievements of {0:X8} failed: {1}", titleId, result);
                return achievements;
            }
            try
            {
                using (NativeBuffer buffer = new NativeBuffer((int)Math.Max(bufferSize, batch * (AchievementSize + AchievementStrings))))
                {
                    uint count;
                    while (Xam.XamEnumerate(handle, 0, buffer.Address, (uint)buffer.Length, out count, 0) == Xam.ErrorSuccess && count > 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int o = i * AchievementSize;
                            achievements.Add(new Achievement
                            {
                                Id = buffer.U32(o + 0x00),
                                Label = buffer.Utf16At(buffer.U32(o + 0x04)) ?? "",
                                Description = buffer.Utf16At(buffer.U32(o + 0x08)) ?? "",
                                Unachieved = buffer.Utf16At(buffer.U32(o + 0x0C)) ?? "",
                                ImageId = buffer.U32(o + 0x10),
                                Gamerscore = (int)buffer.U32(o + 0x14),
                                Unlocked = FileTime(buffer.U64(o + 0x18)),
                                Flags = buffer.U32(o + 0x20),
                            });
                        }
                    }
                }
            }
            finally
            {
                Kernel.NtClose(handle);
            }
            return achievements;
        }

        /// <summary>Opens the console's sign-in screen.</summary>
        public void ShowSignin()
        {
            Xam.XamShowSigninUI(1, 0);
        }

        public void ShowGamerCard(Gamer gamer)
        {
            Xam.XamShowGamerCardUI(gamer.UserIndex);
        }

        public void ShowFriends(Gamer gamer)
        {
            Xam.XamShowFriendsUI(gamer.UserIndex);
        }

        private static DateTime? FileTime(ulong value)
        {
            return FileTimes.Local(value);
        }
    }
}
