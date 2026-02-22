using MainCore.Enums;
using System.Collections.Generic;
using System;

namespace MainCore.Calculators
{
    public static class OasisCombatCalculator
    {
        // 1. ATRIBUTOS OFICIAIS DOS ANIMAIS
        private static readonly Dictionary<string, (int InfDef, int CavDef, int Exp)> AnimalStats = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Rats",       (25, 20, 1) }, { "Spiders",    (35, 40, 1) },
            { "Snakes",     (40, 60, 1) }, { "Bats",       (66, 50, 1) },
            { "Wild Boars", (70, 33, 2) }, { "Wolves",     (80, 70, 2) },
            { "Bears",      (140, 200, 3) }, { "Crocodiles", (380, 240, 3) },
            { "Tigers",     (170, 250, 3) }, { "Elephants",  (440, 520, 5) }
        };

        // 2. TABELA COMPLETA DE TODAS AS TRIBOS
        public static (int AttackPower, bool IsCavalry) GetTroopBaseStats(TribeEnums tribe, int troopIndex)
        {
            return tribe switch
            {
                TribeEnums.Romans => troopIndex switch
                {
                    1 => (40, false),
                    2 => (30, false),
                    3 => (70, false),
                    5 => (120, true),
                    6 => (180, true),
                    _ => (10, false)
                },
                TribeEnums.Teutons => troopIndex switch
                {
                    1 => (40, false),
                    2 => (10, false),
                    3 => (60, false),
                    5 => (55, true),
                    6 => (150, true),
                    _ => (10, false)
                },
                TribeEnums.Gauls => troopIndex switch
                {
                    1 => (15, false),
                    2 => (65, false),
                    4 => (90, true),
                    5 => (45, true),
                    6 => (140, true),
                    _ => (10, false)
                },
                _ => (10, false)
            };
        }

        public static (int TotalInfDef, int TotalCavDef, int TotalExp) AnalyzeOasis(Dictionary<string, int> troopsInOasis)
        {
            int inf = 0, cav = 0, exp = 0;
            foreach (var troop in troopsInOasis)
            {
                if (AnimalStats.TryGetValue(troop.Key, out var stats) || AnimalStats.TryGetValue(troop.Key + "s", out stats))
                {
                    inf += stats.InfDef * troop.Value;
                    cav += stats.CavDef * troop.Value;
                    exp += stats.Exp * troop.Value;
                }
            }
            return (inf, cav, exp);
        }

        public static int CalculateTroopsNeeded(int oasisInfDef, int oasisCavDef, int troopAttack, int heroAttack, bool sendHero, bool isCavalryAttack)
        {
            // REGRA: Se não houver animais (defesa = 0), envia o mínimo de 8 tropas
            if (oasisInfDef == 0 && oasisCavDef == 0)
            {
                return 8;
            }

            if (troopAttack <= 0) return 0;

            // Seleciona a defesa baseada no tipo de ataque (Infantaria ou Cavalaria)
            int relevantDefense = isCavalryAttack ? oasisCavDef : oasisInfDef;

            // Multiplica a defesa por 5 (Regra de segurança)
            long requiredAttack = (long)relevantDefense * 5;

            // Se a EXP for baixa, o 'sendHero' virá como FALSE da Task, 
            // então o heroContribution será 0 aqui.
            long heroContribution = sendHero ? heroAttack : 0;
            long attackDeficit = requiredAttack - heroContribution;

            // Se o Herói sozinho já vence (Saldo <= 0), envia 0 tropas adicionais
            if (attackDeficit <= 0) return 0;

            // Calcula a quantidade e garante que, se houver animais, o mínimo também seja respeitado
            int calculatedAmount = (int)Math.Ceiling((double)attackDeficit / troopAttack);

            return Math.Max(calculatedAmount, 8);
        }
    }
}
