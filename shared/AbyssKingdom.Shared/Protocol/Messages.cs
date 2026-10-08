using AbyssKingdom.Shared.City;
using System.Collections.Generic;
using AbyssKingdom.Shared.Expedition;
using AbyssKingdom.Shared.Heroes;

namespace AbyssKingdom.Shared.Protocol
{
    /// <summary>
    /// 連線協定（MVP 用 JSON 文字訊息，之後可換 Protobuf）。
    /// 每則訊息都是一個 Envelope：{ "type": "...", "reqId": 1, "data": {...} }
    /// 客戶端請求帶 reqId，伺服器回應用相同 reqId；伺服器主動推播 reqId = 0。
    /// </summary>
    public static class MessageTypes
    {
        // 客戶端 → 伺服器
        public const string Login = "login";
        public const string CityGet = "city.get";
        public const string CityUpgrade = "city.upgrade";
        public const string CityTrain = "city.train";
        public const string CityCancel = "city.cancel";
        public const string CitySpeedup = "city.speedup";
        public const string HeroLevelUp = "hero.levelUp";
        public const string GearEquip = "gear.equip";
        public const string GearUnequip = "gear.unequip";
        public const string ExpeditionBattle = "expedition.battle";
        public const string ExpeditionClaimIdle = "expedition.claimIdle";
        public const string DomainUseScroll = "domain.useScroll";
        public const string DomainTrial = "domain.trial";
        public const string HeroStarUp = "hero.starUp";
        public const string Ping = "ping";

        // 伺服器 → 客戶端
        public const string LoginOk = "login.ok";
        public const string CityState = "city.state";
        public const string UpgradeCompleted = "city.upgradeCompleted";
        public const string TrainingCompleted = "city.trainingCompleted";
        public const string ExpeditionResultMessage = "expedition.result";
        public const string ExpeditionIdleClaimed = "expedition.idleClaimed";
        public const string DomainTrialResultMessage = "domain.trialResult";
        public const string Error = "error";
        public const string Pong = "pong";
    }

    public static class ErrorCodes
    {
        public const string NotLoggedIn = "not_logged_in";
        public const string AuthFailed = "auth_failed";
        public const string BadRequest = "bad_request";
        public const string UnknownType = "unknown_type";
        public const string UpgradeRejected = "upgrade_rejected";
        public const string TrainRejected = "train_rejected";
        public const string CancelRejected = "cancel_rejected";
        public const string SpeedupRejected = "speedup_rejected";
        public const string HeroRejected = "hero_rejected";
        public const string GearRejected = "gear_rejected";
        public const string ExpeditionRejected = "expedition_rejected";
        public const string DomainRejected = "domain_rejected";
    }

    public class LoginRequest
    {
        /// <summary>Firebase ID Token；開發模式下可用 "dev:任意名字"。</summary>
        public string Token { get; set; }
        public string DisplayName { get; set; }
    }

    public class LoginOk
    {
        public string PlayerId { get; set; }
        public string DisplayName { get; set; }
        public long ServerTimeMs { get; set; }
    }

    public class UpgradeRequest
    {
        public BuildingType Building { get; set; }
    }

    public class TrainRequest
    {
        public TroopType Troop { get; set; }
        public long Count { get; set; }
    }

    public class CancelRequest
    {
        public QueueType Queue { get; set; }
    }

    /// <summary>Item 為 null 代表免費加速（剩餘 5 分鐘內）；否則使用 Count 個該道具。</summary>
    public class SpeedupRequest
    {
        public QueueType Queue { get; set; }
        public ItemType? Item { get; set; }
        public long Count { get; set; } = 1;
    }

    /// <summary>對主將使用 Count 本經驗書。</summary>
    public class HeroLevelUpRequest
    {
        public HeroId Hero { get; set; }
        public ItemType Item { get; set; }
        public long Count { get; set; } = 1;
    }

    /// <summary>把背包中的某件裝備（Uid）穿到主將身上。</summary>
    public class GearEquipRequest
    {
        public long Gear { get; set; }
        public HeroId Hero { get; set; }
    }

    public class GearUnequipRequest
    {
        public long Gear { get; set; }
    }

    /// <summary>挑戰遠征關卡。Teams：每條岔路一支隊伍（主將清單）。</summary>
    public class ExpeditionBattleRequest
    {
        public int Stage { get; set; }
        public List<List<HeroId>> Teams { get; set; }
    }

    public class DomainScrollRequest
    {
        public HeroId Hero { get; set; }
        public ItemType Item { get; set; }
        public long Count { get; set; } = 1;
    }

    public class HeroStarUpRequest
    {
        public HeroId Hero { get; set; }
    }

    public class DomainTrialRequest
    {
        public HeroId Hero { get; set; }
        public Domain Domain { get; set; }
    }

    public class TrainingCompletedMessage
    {
        public TroopType Troop { get; set; }
        public long Count { get; set; }
    }

    public class CityStateMessage
    {
        public long ServerTimeMs { get; set; }
        public CityState City { get; set; }
    }

    public class UpgradeCompletedMessage
    {
        public BuildingType Building { get; set; }
        public int Level { get; set; }
    }

    public class ErrorMessage
    {
        public string Code { get; set; }
        public string Detail { get; set; }
    }
}
