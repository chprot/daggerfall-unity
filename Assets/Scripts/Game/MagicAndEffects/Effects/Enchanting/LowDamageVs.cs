// Project:         Daggerfall Tools For Unity
// Copyright:       Copyright (C) 2009-2020 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net)
// Contributors:    
// 
// Notes:
//

using System.Collections.Generic;
using DaggerfallConnect.FallExe;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;

namespace DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects
{
    /// <summary>
    /// Lower damage vs enemy types.
    /// TODO: Find correct damage reduction amount.
    /// </summary>
    public class LowDamageVs : BaseEntityEffect
    {
        public static readonly string EffectKey = EnchantmentTypes.LowDamageVs.ToString();

        const int reduceDamageAmount = -5;

        public override void SetProperties()
        {
            properties.Key = EffectKey;
            properties.ShowSpellIcon = false;
            properties.AllowedCraftingStations = MagicCraftingStations.ItemMaker;
            properties.ItemMakerFlags = ItemMakerFlags.AllowMultiplePrimaryInstances | ItemMakerFlags.WeaponOnly;
            properties.EnchantmentPayloadFlags = EnchantmentPayloadFlags.Strikes;
        }

        public override string GroupName { get { return TextManager.Instance.GetLocalizedText(EffectKey); } }

        /// <summary>
        /// Outputs all variant settings for this enchantment.
        /// </summary>
        public override EnchantmentSettings[] GetEnchantmentSettings()
        {
            List<EnchantmentSettings> enchantments = new List<EnchantmentSettings>();

            // Enumerate classic params
            for (int i = 0; i < classicParamCosts.Length; i++)
            {
                EnchantmentSettings enchantment = new EnchantmentSettings()
                {
                    Version = 1,
                    EffectKey = EffectKey,
                    ClassicType = EnchantmentTypes.LowDamageVs,
                    ClassicParam = (short)i,
                    PrimaryDisplayName = GroupName,
                    SecondaryDisplayName = TextManager.Instance.GetLocalizedText(classicTextKeys[i]),
                    EnchantCost = classicParamCosts[i],
                };

                enchantments.Add(enchantment);
            }

            return enchantments.ToArray();
        }

        #region Payloads

        public override PayloadCallbackResults? EnchantmentPayloadCallback(EnchantmentPayloadFlags context, EnchantmentParam? param = null, DaggerfallEntityBehaviour sourceEntity = null, DaggerfallEntityBehaviour targetEntity = null, DaggerfallUnityItem sourceItem = null, int sourceDamage = 0)
        {
            base.EnchantmentPayloadCallback(context, param, sourceEntity, targetEntity, sourceItem);

            // Requires param
            if (param == null)
                return null;

            // Check target is an enemy type
            EnemyEntity enemyEntity = null;
            if (targetEntity != null && (targetEntity.EntityType == EntityTypes.EnemyMonster || targetEntity.EntityType == EntityTypes.EnemyClass))
                enemyEntity = targetEntity.Entity as EnemyEntity;
            else
                return null;

            // Check enemy matches param type
            Params type = (Params)param.Value.ClassicParam;
            if (type == Params.Undead && enemyEntity.MobileEnemy.Affinity == MobileAffinity.Undead ||
                type == Params.Daedra && enemyEntity.MobileEnemy.Affinity == MobileAffinity.Daedra ||
                type == Params.Humanoid && enemyEntity.MobileEnemy.Affinity == MobileAffinity.Human ||
                type == Params.Animals && enemyEntity.MobileEnemy.Affinity == MobileAffinity.Animal)
            {
                // Modulating damage to a lower value
                // Currently unknown what values classic uses to lower damage
                return new PayloadCallbackResults()
                {
                    strikesModulateDamage = reduceDamageAmount
                };
            }

            return null;
        }

        public override bool IsEnchantmentExclusiveTo(EnchantmentSettings[] settingsToTest, EnchantmentParam? comparerParam = null)
        {
            string potentVsKey = EnchantmentTypes.PotentVs.ToString();
            foreach (EnchantmentSettings settings in settingsToTest)
            {
                // Exclusive with opposing PotentVs param
                if (settings.EffectKey == potentVsKey && comparerParam != null && settings.ClassicParam == comparerParam.Value.ClassicParam)
                    return true;
            }

            return false;
        }

        #endregion

        #region Classic Support

        public enum Params
        {
            Undead,
            Daedra,
            Humanoid,
            Animals,
        }

        static short[] classicParamCosts =
        {
            -800,   //Undead
            -900,   //Daedra
            -1000,  //Humanoid
            -1200,  //Animals
        };

        static string[] classicTextKeys =
        {
            "undead",
            "daedra",
            "humanoid",
            "animalsUpper",
        };

        #endregion
    }
}