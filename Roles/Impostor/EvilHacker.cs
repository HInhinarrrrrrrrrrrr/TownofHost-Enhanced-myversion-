using System;
using System.Text;
using Hazel;
using TOHE.Modules;
using TOHE.Roles.Core;
using UnityEngine;
using static TOHE.Options;

namespace TOHE.Roles.Impostor;

// Ported from: https://github.com/tukasa0001/TownOfHost/blob/main/Roles/Impostor/EvilHacker.cs
internal class EvilHacker : RoleBase
{
    //===========================SETUP================================\\
    private const int Id = 28400;
    private static readonly HashSet<byte> PlayerIds = [];
    public static bool HasEnabled => PlayerIds.Any();
    public override bool IsEnable => HasEnabled;
    public override CustomRoles ThisRoleBase => CustomRoles.Impostor;
    public override Custom_RoleType ThisRoleType => Custom_RoleType.ImpostorKilling;
    //==================================================================\\

    private static OptionItem OptionCanSeeDeadMark;
    private static OptionItem OptionCanSeeImpostorMark;
    private static OptionItem OptionCanSeeKillFlash;
    private static OptionItem OptionCanSeeMurderRoom;

    public enum OptionName
    {
        EvilHackerCanSeeDeadMark,
        EvilHackerCanSeeImpostorMark,
        EvilHackerCanSeeKillFlash,
        EvilHackerCanSeeMurderRoom,
    }
    private static bool canSeeDeadMark;
    private static bool canSeeImpostorMark;
    private static bool canSeeKillFlash;
    private static bool canSeeMurderRoom;

    private static PlayerControl evilHackerPlayer = null;
    private static readonly HashSet<MurderNotify> activeNotifies = new(2);

    public override void SetupCustomOption()
    {
        SetupSingleRoleOptions(Id, TabGroup.ImpostorRoles, CustomRoles.EvilHacker);
        OptionCanSeeDeadMark = BooleanOptionItem.Create(Id + 10, OptionName.EvilHackerCanSeeDeadMark, true, TabGroup.ImpostorRoles, false)
            .SetParent(CustomRoleSpawnChances[CustomRoles.EvilHacker]);
        OptionCanSeeImpostorMark = BooleanOptionItem.Create(Id + 11, OptionName.EvilHackerCanSeeImpostorMark, true, TabGroup.ImpostorRoles, false)
            .SetParent(CustomRoleSpawnChances[CustomRoles.EvilHacker]);
        OptionCanSeeKillFlash = BooleanOptionItem.Create(Id + 12, OptionName.EvilHackerCanSeeKillFlash, true, TabGroup.ImpostorRoles, false)
            .SetParent(CustomRoleSpawnChances[CustomRoles.EvilHacker]);
        OptionCanSeeMurderRoom = BooleanOptionItem.Create(Id + 13, OptionName.EvilHackerCanSeeMurderRoom, true, TabGroup.ImpostorRoles, false)
            .SetParent(OptionCanSeeKillFlash);
    }
    public override void Init()
    {
        PlayerIds.Clear();
        evilHackerPlayer = null;

        canSeeDeadMark = OptionCanSeeDeadMark.GetBool();
        canSeeImpostorMark = OptionCanSeeImpostorMark.GetBool();
        canSeeKillFlash = OptionCanSeeKillFlash.GetBool();
        canSeeMurderRoom = OptionCanSeeMurderRoom.GetBool();
    }
    public override void Add(byte playerId)
    {
        PlayerIds.Add(playerId);
        evilHackerPlayer = Utils.GetPlayerById(playerId);

        CustomRoleManager.CheckDeadBodyOthers.Add(HandleMurderRoomNotify);
    }

    private static void HandleMurderRoomNotify(PlayerControl killer, PlayerControl target, bool inMeeting)
    {
        if (canSeeMurderRoom)
        {
            OnMurderPlayer(killer, target, inMeeting);
        }
    }
    public override void OnReportDeadBody(PlayerControl reporter, PlayerControl target)
    {
        if (!evilHackerPlayer.IsAlive())
        {
            return;
        }
        var admins = AdminProvider.CalculateAdmin();
        var builder = new StringBuilder(512);

        foreach (var admin in admins)
        {
            var entry = admin.Value;
            if (entry.TotalPlayers <= 0)
            {
                continue;
            }

            if (canSeeImpostorMark && entry.NumImpostors > 0)
            {
                var ImpostorMark = "★".Color(Palette.ImpostorRed);
                builder.Append(ImpostorMark);
            }

            builder.Append(DestroyableSingleton<TranslationController>.Instance.GetString(entry.Room));
            builder.Append(": ");
            builder.Append(entry.TotalPlayers);

            if (canSeeDeadMark && entry.NumDeadBodies > 0)
            {
                builder.Append(' ').Append('(').Append(Translator.GetString("EvilHackerDeadbody"));
                builder.Append('×').Append(entry.NumDeadBodies).Append(')');
            }
            builder.Append('\n');
        }

        var message = builder.ToString();
        var title = Utils.ColorString(Color.green, Translator.GetString("EvilHackerLastAdminInfoTitle"));

        _ = new LateTask(() =>
        {
            if (GameStates.IsInGame)
            {
                Utils.SendMessage(message, evilHackerPlayer.PlayerId, title, false);
            }
        }, 5f, "EvilHacker Admin Message");
        return;
    }
    private static void OnMurderPlayer(PlayerControl killer, PlayerControl target, bool inMeeting)
    {
        if (!evilHackerPlayer.IsAlive() || !CheckKillFlash(killer, target, inMeeting) || killer.PlayerId == evilHackerPlayer.PlayerId)
        {
            return;
        }
        RpcCreateMurderNotify(target.GetPlainShipRoom()?.RoomId ?? SystemTypes.Hallway);
    }

    public override bool KillFlashCheck(PlayerControl killer, PlayerControl target, PlayerControl seer)
        => CheckKillFlash(killer, target, GameStates.IsMeeting);

    private static void RpcCreateMurderNotify(SystemTypes room)
    {
        CreateMurderNotify(room);
        if (AmongUsClient.Instance.AmHost)
        {
            SendRPC(2, room);
        }
    }
    private static void SendRPC(byte RpcTypeId, SystemTypes room)
    {
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SyncRoleSkill, SendOption.Reliable, -1);
        writer.WritePacked((int)CustomRoles.EvilHacker);
        writer.Write(RpcTypeId);
        writer.Write((byte)room);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    public override void ReceiveRPC(MessageReader reader, PlayerControl pc)
    {
        var RpcTypeId = reader.ReadByte();

        switch (RpcTypeId)
        {
            case 0:
                foreach (var notify in activeNotifies)
                {
                    if (DateTime.Now - notify.CreatedAt > NotifyDuration)
                    {
                        activeNotifies.Remove(notify);
                    }
                }
                break;
            case 1:
                activeNotifies.Add(new()
                {
                    CreatedAt = DateTime.Now,
                    Room = (SystemTypes)reader.ReadByte(),
                });
                break;
            case 2:
                CreateMurderNotify((SystemTypes)reader.ReadByte());
                break;
        }
    }

    private static void CreateMurderNotify(SystemTypes room)
    {
        activeNotifies.Add(new()
        {
            CreatedAt = DateTime.Now,
            Room = room,
        });
        if (AmongUsClient.Instance.AmHost)
        {
            Utils.NotifyRoles(SpecifySeer: evilHackerPlayer);
            SendRPC(1, room);
        }
    }
    public override void OnFixedUpdateLowLoad(PlayerControl pc)
    {
        if (evilHackerPlayer != null && evilHackerPlayer != PlayerControl.LocalPlayer)
        {
            return;
        }
        if (activeNotifies.Count <= 0)
        {
            return;
        }

        var doNotifyRoles = false;
        foreach (var notify in activeNotifies)
        {
            if (DateTime.Now - notify.CreatedAt > NotifyDuration)
            {
                activeNotifies.Remove(notify);
                SendRPC(0, SystemTypes.Hallway);
                doNotifyRoles = true;
            }
        }
        if (doNotifyRoles)
        {
            Utils.NotifyRoles(SpecifySeer: evilHackerPlayer);
        }
    }
    public override string GetSuffix(PlayerControl seer, PlayerControl seen, bool isForMeeting = false)
    {
        if (isForMeeting || !canSeeMurderRoom || evilHackerPlayer == null || seer != evilHackerPlayer || seen != evilHackerPlayer || activeNotifies.Count <= 0)
        {
            return string.Empty;
        }
        var roomNames = activeNotifies.Select(notify => DestroyableSingleton<TranslationController>.Instance.GetString(notify.Room));
        return Utils.ColorString(Color.green, $"{Translator.GetString("EvilHackerMurderNotify")}: {string.Join(", ", roomNames)}");
    }

    public static bool CheckKillFlash(PlayerControl killer, PlayerControl target, bool inMeeting)
        => canSeeKillFlash && killer.PlayerId != target.PlayerId && !inMeeting;


    private static readonly TimeSpan NotifyDuration = TimeSpan.FromSeconds(10);
    private readonly struct MurderNotify
    {
        public DateTime CreatedAt { get; init; }
        public SystemTypes Room { get; init; }
    }
}
