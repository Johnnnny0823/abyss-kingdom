using System;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Shared.Heroes
{
    /// <summary>
    /// 星色（2026-10-08 使用者定案）：金星 → 藍星 → 紅星 → 銀彩星，每色 5 顆。
    /// 第 k 股索的斷索試煉，需要先把第 k 種星色升滿。
    /// </summary>
    public enum StarTier
    {
        Gold = 0,
        Blue = 1,
        Red = 2,
        Prism = 3,  // 銀彩星
    }

    public enum StarResult
    {
        Ok = 0,
        UnknownHero = 1,
        HeroNotOwned = 2,
        MaxStars = 3,
        NotEnoughShards = 4,
        NotEnoughResources = 5,
    }

    public static class StarConfig
    {
        public const int StarsPerTier = 5;
        public static readonly int TierCount = Enum.GetValues(typeof(StarTier)).Length;
        public static int MaxStars => StarsPerTier * TierCount;

        /// <summary>升到第 (stars+1) 顆星所需信物：金 10、藍 20、紅 40、銀彩 80（每顆）。</summary>
        public static long ShardCost(int currentStars) => 10L << TierOf(currentStars);

        /// <summary>升星所需響晶：金 500、藍 1,500、紅 4,000、銀彩 10,000（每顆）。</summary>
        public static long CrystalCost(int currentStars)
        {
            switch (TierOf(currentStars))
            {
                case 0: return 500;
                case 1: return 1_500;
                case 2: return 4_000;
                default: return 10_000;
            }
        }

        /// <summary>下一顆星屬於哪個星色（0 起算）。</summary>
        public static int TierOf(int currentStars) => Math.Min(TierCount - 1, currentStars / StarsPerTier);

        /// <summary>第 tierIndex 種星色是否已升滿。</summary>
        public static bool TierComplete(int stars, int tierIndex) => stars >= StarsPerTier * (tierIndex + 1);

        /// <summary>屬性加成百分比：攻防命每星 +4%、統率每星 +2%。</summary>
        public static long StatPercent(int stars) => 100 + 4L * stars;
        public static long LeadershipPercent(int stars) => 100 + 2L * stars;
    }

    public static class StarLogic
    {
        /// <summary>升一顆星：先用該主將的專屬信物，不足的部分用通用信物補。</summary>
        public static StarResult StarUp(CityState city, HeroId id)
        {
            if (!HeroConfig.TryGet(id, out _)) return StarResult.UnknownHero;
            if (!city.Heroes.TryGetValue(id, out var hero)) return StarResult.HeroNotOwned;
            if (hero.Stars >= StarConfig.MaxStars) return StarResult.MaxStars;

            long need = StarConfig.ShardCost(hero.Stars);
            var own = ItemConfig.ShardFor(id);
            long have = city.GetItemCount(own), any = city.GetItemCount(ItemType.HeroShardAny);
            if (have + any < need) return StarResult.NotEnoughShards;
            long crystal = StarConfig.CrystalCost(hero.Stars);
            if (city.GetResource(ResourceType.Crystal) < crystal) return StarResult.NotEnoughResources;

            long fromOwn = Math.Min(have, need);
            city.Items[own] = have - fromOwn;
            city.Items[ItemType.HeroShardAny] = any - (need - fromOwn);
            city.Resources[ResourceType.Crystal] = city.GetResource(ResourceType.Crystal) - crystal;
            hero.Stars++;
            return StarResult.Ok;
        }
    }
}
