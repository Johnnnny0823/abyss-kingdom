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
        public const string CityMove = "city.move";
        public const string CityHeal = "city.heal";
        public const string HeroLevelUp = "hero.levelUp";
        public const string GearEquip = "gear.equip";
        public const string GearUnequip = "gear.unequip";
        public const string ExpeditionBattle = "expedition.battle";
        public const string ExpeditionClaimIdle = "expedition.claimIdle";
        public const string DomainUseScroll = "domain.useScroll";
        public const string DomainTrial = "domain.trial";
        public const string HeroStarUp = "hero.starUp";
        public const string MapMarch = "map.march";
        public const string MapRecall = "map.recall";
        public const string WorldCities = "world.cities";
        public const string WorldTile = "world.tile";
        public const string CityAttacked = "city.attacked";
        public const string HealingCompleted = "city.healingCompleted";
        public const string Ping = "ping";

        // 伺服器 → 客戶端
        public const string LoginOk = "login.ok";
        public const string CityState = "city.state";
        public const string UpgradeCompleted = "city.upgradeCompleted";
        public const string TrainingCompleted = "city.trainingCompleted";
        public const string ExpeditionResultMessage = "expedition.result";
        public const string ExpeditionIdleClaimed = "expedition.idleClaimed";
        public const string DomainTrialResultMessage = "domain.trialResult";
        public const string MarchArrived = "map.marchArrived";
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
        public const string MapRejected = "map_rejected";
        public const string MoveRejected = "move_rejected";
        public const string HealRejected = "heal_rejected";
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

    /// <summary>伺服器推播（給守方）：主城被攻打的結果。</summary>
    public class CityAttackedMessage
    {
        public string AttackerName { get; set; }
        public bool DefenderWon { get; set; }
        public Dictionary<TroopType, long> Wounded { get; set; }
        public Dictionary<TroopType, long> Dead { get; set; }
        public Dictionary<ResourceType, long> Plundered { get; set; }
    }

    /// <summary>治療醫館裡的傷兵（各兵種數量）。</summary>
    public class HealRequest
    {
        public Dictionary<TroopType, long> Troops { get; set; }
    }

    /// <summary>伺服器推播：治療完成，傷兵回到兵營。</summary>
    public class HealingCompletedMessage
    {
        public Dictionary<TroopType, long> Troops { get; set; }
    }

    /// <summary>把建築移到城內格子 (X, Y)（佔地左上角）。</summary>
    public class MoveBuildingRequest
    {
        public BuildingType Building { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
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

    /// <summary>出征：主將帶兵營部隊，從自己的垂降口前往 Destination。</summary>
    public class MapMarchRequest
    {
        public HeroId Hero { get; set; }
        public Dictionary<TroopType, long> Troops { get; set; }
        /// <summary>目標格子。</summary>
        public int X { get; set; }
        public int Y { get; set; }
    }

    /// <summary>地表上一座主城（給所有玩家看的公開資訊）。</summary>
    public class CityPin
    {
        /// <summary>主城格子座標（佔地左上角，2×2）。</summary>
        public int X { get; set; }
        public int Y { get; set; }
        public string Name { get; set; }
        public long Power { get; set; }
        public bool Mine { get; set; }
        /// <summary>護盾中（不能被攻打）。</summary>
        public bool Shielded { get; set; }
    }

    /// <summary>查詢地表某一格的即時狀態（目前只有資源田的存量與誰在採）。</summary>
    public class TileInfoRequest
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class TileInfoMessage
    {
        public int X { get; set; }
        public int Y { get; set; }
        /// <summary>資源田剩餘存量／最大存量（不是資源田時都是 0）。</summary>
        public long Stock { get; set; }
        public long Max { get; set; }
        /// <summary>正在採集的領主名稱；沒人在採時為 null。</summary>
        public string Occupant { get; set; }
        public bool Mine { get; set; }
        /// <summary>怪物營地被擊敗後重生的時間（Unix 毫秒）；0 = 營地在。</summary>
        public long CampBackAt { get; set; }
    }

    public class WorldCitiesMessage
    {
        public List<CityPin> Cities { get; set; } = new List<CityPin>();
    }

    public class MapRecallRequest
    {
        public long March { get; set; }
    }

    /// <summary>伺服器推播：行軍抵達（駐紮）或召回後解散。</summary>
    public class MarchArrivedMessage
    {
        public long March { get; set; }
        public HeroId Hero { get; set; }
        /// <summary>抵達的格子（SurfaceLogic.Tile 編碼）。</summary>
        public int Tile { get; set; }
        public bool Disbanded { get; set; }
        /// <summary>抵達資源田、開始採集。</summary>
        public bool Gathering { get; set; }
        /// <summary>抵達資源田，但已經有人在採（或採空了）。</summary>
        public bool Occupied { get; set; }
        /// <summary>採滿，自動回城。</summary>
        public bool GatherDone { get; set; }
        /// <summary>帶著（或帶回）的資源。</summary>
        public Dictionary<ResourceType, long> Carry { get; set; }
        /// <summary>帶著（或帶回）的戰利品道具。</summary>
        public Dictionary<ItemType, long> LootItems { get; set; }
        /// <summary>打贏／打輸怪物營地；營地已被打掉（還沒重生）。</summary>
        public bool BattleWon { get; set; }
        public bool BattleLost { get; set; }
        public bool CampGone { get; set; }
        /// <summary>這場戰鬥的傷兵（送進醫館）與陣亡。</summary>
        public Dictionary<TroopType, long> Wounded { get; set; }
        public Dictionary<TroopType, long> Dead { get; set; }
        /// <summary>攻城：對方有護盾，沒有開打。</summary>
        public bool Shielded { get; set; }
        /// <summary>攻城：被攻打的領主名稱（不是攻城時為 null）。</summary>
        public string TargetName { get; set; }
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
