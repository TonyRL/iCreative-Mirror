﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using EloBuddy;
using EloBuddy.SDK;
using EloBuddy.SDK.Events;
using EloBuddy.SDK.Menu.Values;
using EloBuddy.SDK.Rendering;
using Jhin.Managers;
using Jhin.Model;
using Jhin.Utilities;
using SharpDX;

namespace Jhin.Champions
{
    public class Jhin : ChampionBase
    {
        //Jhin Q: CastDelay: 250, Range: 550, CastRadius: 450, MissileSpeed: 600
        //Jhin E: CastDelay = 500, Radius: 135, Range: 750
        public bool IsCastingR;
        public bool TapKeyPressed;
        public int Stacks;
        public int LastBlockTick;
        public int WShouldWaitTick;
        public Geometry.Polygon.Sector LastRCone;
        public Dictionary<int, Text> TextsDictionary = new Dictionary<int, Text>();

        public bool IsR1
        {
            get { return R.Instance.SData.Name == "JhinR"; }
        }

        public Jhin()
        {
            foreach (var enemy in EntityManager.Heroes.Enemies)
            {
                TextsDictionary.Add(enemy.NetworkId, new Text(enemy.ChampionName + " Is R Killable", new Font("Arial", 30F, FontStyle.Bold)));
            }
            Q = new SpellBase(SpellSlot.Q, SpellType.Targeted, 550)
            {
                Width = 450,
                Speed = 1800,
                CastDelay = 250,
            };
            W = new SpellBase(SpellSlot.W, SpellType.Linear, 3000)
            {
                Width = 40,
                CastDelay = 750,
            };
            E = new SpellBase(SpellSlot.E, SpellType.Circular, 750)
            {
                Width = 135,
                CastDelay = 500,
            };
            R = new SpellBase(SpellSlot.R, SpellType.Linear, 3500)
            {
                Width = 80,
                CastDelay = 200,
                Speed = 5000,
                AllowedCollisionCount = -1,
            };
            Evader.OnEvader += delegate
            {
                if (EvaderMenu.CheckBox("BlockW"))
                {
                    LastBlockTick = Core.GameTickCount;
                }
            };

            Spellbook.OnCastSpell += delegate (Spellbook sender, SpellbookCastSpellEventArgs args)
            {
                if (sender.Owner.IsMe)
                {
                    if (args.Slot == SpellSlot.W)
                    {
                        args.Process = Core.GameTickCount - LastBlockTick > 750;
                    }
                }
            };
            Obj_AI_Base.OnProcessSpellCast += delegate (Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
            {
                if (sender.IsMe)
                {
                    switch (args.Slot)
                    {
                        case SpellSlot.W:
                            W.LastCastTime = Core.GameTickCount;
                            W.LastEndPosition = args.End;
                            break;
                        case SpellSlot.R:
                            if (args.SData.Name == "JhinR")
                            {
                                IsCastingR = true;
                                LastRCone = new Geometry.Polygon.Sector(sender.Position, args.End, (float)(45 * 2f * Math.PI / 180f), R.Range);
                                Stacks = 4;
                            }
                            else if (args.SData.Name == "JhinRShot")
                            {
                                R.LastCastTime = Core.GameTickCount;
                                Stacks--;
                            }
                            break;
                    }
                }
            };
            Gapcloser.OnGapcloser += delegate (AIHeroClient sender, Gapcloser.GapcloserEventArgs args)
            {
                if (sender.IsValidTarget() && sender.IsEnemy)
                {
                    if (MyHero.Distance(args.Start, true) > MyHero.Distance(args.End))
                    {
                        if (AutomaticMenu.CheckBox("E.Gapcloser") && MyHero.IsInRange(args.End, E.Range))
                        {
                            E.Cast(args.End);
                        }
                        if (MyHero.Distance(args.End, true) < (sender.GetAutoAttackRange(MyHero) * 1.5f).Pow())
                        {
                            WShouldWaitTick = Core.GameTickCount;
                        }
                    }
                }
            };

            MenuManager.AddSubMenu("Keys");
            {
                KeysMenu.AddValue("TapKey", new KeyBind("R Tap Key", false, KeyBind.BindTypes.HoldActive, 'T')).OnValueChange +=
                    delegate (ValueBase<bool> sender, ValueBase<bool>.ValueChangeArgs args)
                    {
                        if (args.NewValue && R.IsLearned && (R.IsReady || IsCastingR) && R.EnemyHeroes.Count > 0)
                        {
                            TapKeyPressed = true;
                        }
                    };
            }

            W.AddConfigurableHitChancePercent();
            R.AddConfigurableHitChancePercent();

            MenuManager.AddSubMenu("Combo");
            {
                ComboMenu.AddValue("Q", new CheckBox("Use Q"));
                ComboMenu.AddValue("W", new CheckBox("Use W"));
                ComboMenu.AddValue("E", new CheckBox("Use E"));
                ComboMenu.AddValue("Items", new CheckBox("Use offensive items"));
            }
            MenuManager.AddSubMenu("Ultimate");
            {
                UltimateMenu.AddStringList("Mode", "R AIM Mode", new[] { "Disabled", "Using TapKey", "Automatic" }, 2);
                UltimateMenu.AddValue("OnlyKillable", new CheckBox("Only attack if it's killable", false));
                UltimateMenu.AddValue("Delay", new Slider("Delay between R's (in ms)", 0, 0, 1500));
                UltimateMenu.AddValue("NearMouse", new GroupLabel("Near Mouse Settings"));
                UltimateMenu.AddValue("NearMouse.Enabled", new CheckBox("Only select target near mouse", false));
                UltimateMenu.AddValue("NearMouse.Radius", new Slider("Near mouse radius", 500, 100, 1500));
                UltimateMenu.AddValue("NearMouse.Draw", new CheckBox("Draw near mouse radius"));
            }
            MenuManager.AddSubMenu("Harass");
            {
                HarassMenu.AddValue("Q", new CheckBox("Use Q"));
                HarassMenu.AddValue("W", new CheckBox("Use W", false));
                HarassMenu.AddValue("E", new CheckBox("Use E", false));
                HarassMenu.AddValue("ManaPercent", new Slider("Minimum Mana Percent", 20));
            }

            MenuManager.AddSubMenu("Clear");
            {
                ClearMenu.AddValue("LaneClear", new GroupLabel("LaneClear"));
                {
                    ClearMenu.AddValue("LaneClear.Q", new Slider("Use Q if hit is greater than {0}", 3, 0, 10));
                    ClearMenu.AddValue("LaneClear.W", new Slider("Use W if hit is greater than {0}", 5, 0, 10));
                    ClearMenu.AddValue("LaneClear.E", new Slider("Use E if hit is greater than {0}", 4, 0, 10));
                    ClearMenu.AddValue("LaneClear.ManaPercent", new Slider("Minimum Mana Percent", 50));
                }
                ClearMenu.AddValue("LastHit", new GroupLabel("LastHit"));
                {
                    ClearMenu.AddStringList("LastHit.Q", "Use Q", new[] { "Never", "Smartly", "Always" }, 1);
                    ClearMenu.AddValue("LastHit.ManaPercent", new Slider("Minimum Mana Percent", 50));
                }
                ClearMenu.AddValue("JungleClear", new GroupLabel("JungleClear"));
                {
                    ClearMenu.AddValue("JungleClear.Q", new CheckBox("Use Q"));
                    ClearMenu.AddValue("JungleClear.W", new CheckBox("Use W"));
                    ClearMenu.AddValue("JungleClear.E", new CheckBox("Use W"));
                    ClearMenu.AddValue("JungleClear.ManaPercent", new Slider("Minimum Mana Percent", 20));
                }
            }

            MenuManager.AddKillStealMenu();
            {
                KillStealMenu.AddValue("Q", new CheckBox("Use Q"));
                KillStealMenu.AddValue("W", new CheckBox("Use W"));
                KillStealMenu.AddValue("E", new CheckBox("Use E"));
                KillStealMenu.AddValue("R", new CheckBox("Use R", false));
            }

            MenuManager.AddSubMenu("Automatic");
            {
                AutomaticMenu.AddValue("E.Gapcloser", new CheckBox("Use E on hero gapclosing / dashing"));
                AutomaticMenu.AddValue("Immobile", new CheckBox("Use E on hero immobile"));
                AutomaticMenu.AddValue("Buffed", new CheckBox("Use W on hero with buff"));
            }
            MenuManager.AddSubMenu("Evader");
            {
                EvaderMenu.AddValue("BlockW", new CheckBox("Block W to Evade"));
            }
            Evader.Initialize();
            Evader.AddCrowdControlSpells();
            Evader.AddDangerousSpells();
            MenuManager.AddDrawingsMenu();
            {
                Q.AddDrawings();
                W.AddDrawings();
                E.AddDrawings(false);
                R.AddDrawings();
                DrawingsMenu.Add("R.Killable", new CheckBox("Draw text if target is r killable"));
            }
        }

        public override void OnEndScene()
        {
            if (R.IsReady)
            {
                var count = 0;
                foreach (var enemy in R.EnemyHeroes.Where(h => R.IsKillable(h) && TextsDictionary.ContainsKey(h.NetworkId)))
                {
                    TextsDictionary[enemy.NetworkId].Position = new Vector2(100, 50 * count);
                    TextsDictionary[enemy.NetworkId].Draw();
                    count++;
                }
            }
            base.OnEndScene();
        }

        public override void OnDraw()
        {
            if (UltimateMenu.CheckBox("NearMouse.Enabled") && UltimateMenu.CheckBox("NearMouse.Draw") && IsCastingR)
            {
                EloBuddy.SDK.Rendering.Circle.Draw(SharpDX.Color.Blue, UltimateMenu.Slider("NearMouse.Radius"), 1, MousePos);
            }
            base.OnDraw();
        }

        protected override void PermaActive()
        {
            if (IsCastingR)
            {
                IsCastingR = R.Instance.SData.Name == "JhinRShot"; //MyHero.Spellbook.IsChanneling;
            }
            Orbwalker.DisableAttacking = IsCastingR;
            Orbwalker.DisableMovement = IsCastingR;
            if (R.IsReady && !IsCastingR)
            {
                Stacks = 4;
            }
            Range = 1300;
            Target = TargetSelector.GetTarget(UnitManager.ValidEnemyHeroesInRange, DamageType.Physical);
            Q.Type = SpellType.Targeted;
            if (IsCastingR)
            {
                if (TapKeyPressed || UltimateMenu.Slider("Mode") == 2)
                {
                    CastR();
                }
                return;
            }
            if (AutomaticMenu.CheckBox("Buffed"))
            {
                foreach (var enemy in UnitManager.ValidEnemyHeroes.Where(TargetHaveEBuff))
                {
                    CastW(enemy);
                }
            }
            if (AutomaticMenu.CheckBox("Immobile"))
            {
                foreach (var enemy in E.EnemyHeroes)
                {
                    var time = enemy.GetMovementBlockedDebuffDuration();
                    if (time > 0 && time * 1000 >= E.CastDelay)
                    {
                        E.Cast(enemy.Position);
                    }
                }
            }
            base.PermaActive();
        }

        protected override void KillSteal()
        {
            foreach (var enemy in UnitManager.ValidEnemyHeroesInRange.Where(h => h.HealthPercent <= 40f))
            {
                var result = GetBestCombo(enemy);
                if (KillStealMenu.CheckBox("Q") && (result.Q || Q.IsKillable(enemy)))
                {
                    CastQ(enemy);
                }
                if (KillStealMenu.CheckBox("W") && (result.W || W.IsKillable(enemy)))
                {
                    CastW(enemy);
                }
                if (KillStealMenu.CheckBox("E") && (result.E || E.IsKillable(enemy)))
                {
                    CastE(enemy);
                }
                if (KillStealMenu.CheckBox("R") && IsCastingR && enemy.TotalShieldHealth() <= GetCurrentShotDamage(enemy))
                {
                    CastR();
                }
            }
            base.KillSteal();
        }

        protected override void Combo()
        {
            if (Target != null)
            {
                if (ComboMenu.CheckBox("E"))
                {
                    CastE(Target);
                }
                if (ComboMenu.CheckBox("W"))
                {
                    CastW(Target);
                }
                if (ComboMenu.CheckBox("Q"))
                {
                    CastQ(Target);
                }
            }
            base.Combo();
        }

        protected override void Harass()
        {
            if (MyHero.ManaPercent >= HarassMenu.Slider("ManaPercent"))
            {
                if (Target != null)
                {
                    if (HarassMenu.CheckBox("Q"))
                    {
                        CastQ(Target);
                    }
                    if (HarassMenu.CheckBox("W"))
                    {
                        CastW(Target);
                    }
                    if (HarassMenu.CheckBox("E"))
                    {
                        CastE(Target);
                    }
                }
            }
            base.Harass();
        }

        protected override void LaneClear()
        {
            if (MyHero.ManaPercent >= ClearMenu.Slider("LaneClear.ManaPercent"))
            {
                Q.Type = SpellType.Circular;
                var minion = Q.LaneClear(false, ClearMenu.Slider("LaneClear.Q"));
                Q.Type = SpellType.Targeted;
                CastQ(minion);
                W.LaneClear(true, ClearMenu.Slider("LaneClear.W"));
                E.LaneClear(true, ClearMenu.Slider("LaneClear.E"));
            }
            base.LaneClear();
        }

        protected override void LastHit()
        {
            if (MyHero.ManaPercent >= ClearMenu.Slider("LastHit.ManaPercent"))
            {
                Q.LastHit((LastHitType)ClearMenu.Slider("LastHit.Q"));
            }
            base.LastHit();
        }
        protected override void JungleClear()
        {
            if (MyHero.ManaPercent >= ClearMenu.Slider("JungleClear.ManaPercent"))
            {
                if (ClearMenu.CheckBox("JungleClear.Q"))
                {
                    Q.JungleClear();
                }
                if (ClearMenu.CheckBox("JungleClear.W"))
                {
                    W.JungleClear();
                }
                if (ClearMenu.CheckBox("JungleClear.E"))
                {
                    E.JungleClear();
                }
            }
            base.JungleClear();
        }

        public void CastQ(Obj_AI_Base target)
        {
            if (Q.IsReady && target != null)
            {
                Q.Type = SpellType.Circular;
                Q.EnemyMinionsCanBeCalculated = true;
                Q.LaneClearMinionsCanBeCalculated = true;
                var bestMinion = Q.LaneClear(false);
                if (bestMinion != null && bestMinion.IsInRange(target, Q.Width) &&
                    Q.EnemyMinions.Count(o => o.IsInRange(bestMinion, Q.Width)) <= 3)
                {
                    Q.Type = SpellType.Targeted;
                    Q.Cast(target);
                }
                else
                {
                    Q.Type = SpellType.Targeted;
                    if (Q.InRange(target))
                    {
                        Q.Cast(target);
                    }
                }
                Q.Type = SpellType.Targeted;
            }
        }
        public void CastW(Obj_AI_Base target)
        {
            if (W.IsReady && target != null)
            {
                if (Core.GameTickCount - LastBlockTick < 750)
                {
                    return;
                }
                if (Core.GameTickCount - WShouldWaitTick < 750)
                {
                    return;
                }
                if (MyHero.CountEnemiesInRange(400) != 0)
                {
                    return;
                }
                var hero = target as AIHeroClient;
                if (hero != null)
                {
                    if (MyHero.IsInAutoAttackRange(target) && !TargetHaveEBuff(hero))
                    {
                        return;
                    }
                }
                W.Cast(target);
            }
        }
        public void CastE(Obj_AI_Base target)
        {
            if (E.IsReady && target != null)
            {
                E.Cast(target);
            }
        }
        public void CastR()
        {
            if (!IsR1 && Core.GameTickCount - R.LastCastTime >= UltimateMenu.Slider("Delay"))
            {
                var rTargets = UnitManager.ValidEnemyHeroes.Where(h =>  R.InRange(h) && (!AutomaticMenu.CheckBox("OnlyKillable") || R.IsKillable(h)) && LastRCone.IsInside(h)).ToList();
                var targets = UltimateMenu.CheckBox("NearMouse.Enabled")
                    ? rTargets.Where(
                        h => h.IsInRange(MousePos, UltimateMenu.Slider("NearMouse.Radius"))).ToList()
                    : rTargets;
                var target = TargetSelector.GetTarget(targets, DamageType.Physical);
                if (target != null)
                {
                    var pred = R.GetPrediction(target);
                    Chat.Print(pred.HitChancePercent);
                    if (pred.HitChancePercent >= R.HitChancePercent)
                    {
                        MyHero.Spellbook.CastSpell(SpellSlot.R, pred.CastPosition);
                    }
                }
            }
        }

        public bool TargetHaveEBuff(AIHeroClient target)
        {
            return target.TargetHaveBuff("jhinespotteddebuff");
        }

        public override float GetSpellDamage(SpellSlot slot, Obj_AI_Base target)
        {
            if (target != null)
            {
                var level = slot.GetSpellDataInst().Level;
                switch (slot)
                {
                    case SpellSlot.Q:
                        return MyHero.CalculateDamageOnUnit(target, DamageType.Physical,
                            25f * level + 35f + (0.25f + 5f * level) * MyHero.TotalAttackDamage + 0.6f * MyHero.FlatMagicDamageMod);
                    case SpellSlot.W:
                        return 3 * MyHero.CalculateDamageOnUnit(target, DamageType.Physical,
                            (target is AIHeroClient ? 1f : 0.65f) * (35f * level + 15f + 0.7f * MyHero.TotalAttackDamage));
                    case SpellSlot.E:
                        return MyHero.CalculateDamageOnUnit(target, DamageType.Magical,
                            (target is AIHeroClient ? 1f : 0.65f) * (60f * level - 40f + 1.2f * MyHero.TotalAttackDamage + 1f * MyHero.FlatMagicDamageMod));
                    case SpellSlot.R:
                        var shotDamage =
                            MyHero.CalculateDamageOnUnit(target, DamageType.Physical, 75f * level - 25f + 0.25f * MyHero.TotalAttackDamage) * (1f + 0.02f * (target.MaxHealth - target.Health) / target.MaxHealth * 100f);
                        var normalShotsDamage = (Stacks - 1) * shotDamage;
                        var lastShotDamage = (2f + (MyHero.HasItem(ItemId.Infinity_Edge) ? 0.5f : 0f)) * shotDamage;
                        return normalShotsDamage + lastShotDamage;
                }
            }
            return base.GetSpellDamage(slot, target);
        }

        public float GetCurrentShotDamage(Obj_AI_Base target)
        {
            var level = R.Slot.GetSpellDataInst().Level;
            return (Stacks == 1 ? (2f + (MyHero.HasItem(ItemId.Infinity_Edge) ? 0.5f : 0f)) : 1f) *
                   MyHero.CalculateDamageOnUnit(target, DamageType.Physical,
                       75f * level - 25f + 0.25f * MyHero.TotalAttackDamage) *
                   (1f + 0.02f * (target.MaxHealth - target.Health) / target.MaxHealth * 100f);
        }
    }
}
