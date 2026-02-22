using FluentResults;
using MainCore.Calculators;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Tasks.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MainCore.Commands.Navigate;
using MainCore.Services;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace MainCore.Tasks
{
    [Handler]
    public static partial class AttackOasisTask
    {
        public class OasisTarget
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int TroopIndexToUse { get; set; }
            public int MinimumExpForHero { get; set; }
            public int HeroAttackPower { get; set; }
        }

        private class PlannedAttack
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int AmountToSend { get; set; }
            public int TroopIndexToUse { get; set; }
            public bool SendHero { get; set; }
            public int TotalExp { get; set; }
        }

        private static readonly Dictionary<string, (int infDef, int cavDef, int exp)> NatureStats = new()
        {
            { "31", (25, 10, 1) },   // Rato
            { "32", (35, 40, 1) },   // Aranha
            { "33", (40, 60, 1) },   // Cobra
            { "34", (66, 50, 1) },   // Morcego
            { "35", (70, 33, 2) },   // Javali
            { "36", (80, 70, 2) },   // Lobo
            { "37", (140, 200, 3) }, // Urso
            { "38", (380, 240, 3) }, // Crocodilo
            { "39", (170, 250, 3) }, // Tigre
            { "40", (440, 520, 5) }  // Elefante
        };

        public sealed class Task : VillageTask
        {
            public List<OasisTarget> Targets { get; }
            public int TribeInt { get; }

            public Task(AccountId accountId, VillageId villageId, List<OasisTarget> targets, int tribeInt) : base(accountId, villageId)
            {
                Targets = targets;
                TribeInt = tribeInt;
            }

            protected override string TaskName => $"Rodada de Farm ({Targets.Count} alvos)";
            public override bool CanStart(AppDbContext context) => true;
        }

        private static async ValueTask<Result> HandleAsync(
            AttackOasisTask.Task task,
            SwitchVillageCommand.Handler switchVillageCommand,
            IChromeManager chromeManager,
            AppDbContext context,
            ILogger logger,
            ITaskManager taskManager,
            CancellationToken cancellationToken)
        {
            var browser = chromeManager.Get(task.AccountId);
            TribeEnums playerTribe = (TribeEnums)task.TribeInt;
            var plannedAttacks = new List<PlannedAttack>();

            var resultSwitch = await switchVillageCommand.HandleAsync(new(task.VillageId), cancellationToken);
            if (resultSwitch.IsFailed) return resultSwitch;

            Random rnd = new Random();
            logger.Information($"--- FASE 1: Varrendo API para {task.Targets.Count} oásis ---");

            foreach (var target in task.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string jsFetchApi = $$"""
                    var oldDiv = document.getElementById('bot_oasis_data');
                    if (oldDiv) oldDiv.remove();
                    fetch('/api/v1/map/tile-details', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ x: {{target.X}}, y: {{target.Y}} })
                    })
                    .then(res => res.json())
                    .then(data => {
                        let d = document.createElement('div');
                        d.id = 'bot_oasis_data';
                        d.innerHTML = data.html || 'EMPTY';
                        document.body.appendChild(d);
                    })
                    .catch(() => {
                        let d = document.createElement('div');
                        d.id = 'bot_oasis_data';
                        d.innerHTML = 'ERROR';
                        document.body.appendChild(d);
                    });
                """;

                await browser.ExecuteJsScript(jsFetchApi);

                string htmlPayload = "";
                for (int i = 0; i < 20; i++)
                {
                    await System.Threading.Tasks.Task.Delay(150, cancellationToken);
                    var dataNode = browser.Html.DocumentNode.SelectSingleNode("//div[@id='bot_oasis_data']");
                    if (dataNode != null) { htmlPayload = dataNode.InnerHtml; break; }
                }

                if (string.IsNullOrEmpty(htmlPayload) || htmlPayload == "ERROR") continue;

                var docOasis = new HtmlDocument();
                docOasis.LoadHtml(htmlPayload);

                int totalInfDef = 0;
                int totalCavDef = 0;
                int totalExpOasis = 0;

                var troopTable = docOasis.DocumentNode.SelectSingleNode("//table[@id='troop_info']");
                if (troopTable != null)
                {
                    var rows = troopTable.SelectNodes(".//tr");
                    if (rows != null)
                    {
                        foreach (var row in rows)
                        {
                            var iconNode = row.SelectSingleNode(".//*[contains(@class, 'unit')]");
                            var valNode = row.SelectSingleNode(".//td[contains(@class, 'val')]");

                            if (iconNode != null && valNode != null)
                            {
                                string cssClass = iconNode.GetAttributeValue("class", "");
                                var match = Regex.Match(cssClass, @"u(3[1-9]|40)");

                                if (match.Success && int.TryParse(valNode.InnerText.Trim(), out int count))
                                {
                                    string id = match.Groups[1].Value;
                                    if (NatureStats.TryGetValue(id, out var stats))
                                    {
                                        totalInfDef += stats.infDef * count;
                                        totalCavDef += stats.cavDef * count;
                                        totalExpOasis += stats.exp * count;
                                    }
                                }
                            }
                        }
                    }
                }

                var (troopAttackPower, isCavalry) = OasisCombatCalculator.GetTroopBaseStats(playerTribe, target.TroopIndexToUse);
                bool shouldSendHero = totalExpOasis >= target.MinimumExpForHero;

                int amountToSend = OasisCombatCalculator.CalculateTroopsNeeded(totalInfDef, totalCavDef, troopAttackPower, target.HeroAttackPower, shouldSendHero, isCavalry);

                if (amountToSend > 0 || shouldSendHero)
                {
                    plannedAttacks.Add(new PlannedAttack
                    {
                        X = target.X,
                        Y = target.Y,
                        AmountToSend = amountToSend,
                        TroopIndexToUse = target.TroopIndexToUse,
                        SendHero = shouldSendHero,
                        TotalExp = totalExpOasis
                    });
                    logger.Information($"Lido ({target.X}|{target.Y}): EXP:{totalExpOasis}. Planejado: {amountToSend} T{target.TroopIndexToUse} {(shouldSendHero ? "+ Herói" : "")}");
                }

                await System.Threading.Tasks.Task.Delay(rnd.Next(1000, 2000), cancellationToken);
            }

            if (plannedAttacks.Count == 0)
            {
                int skipCycle = rnd.Next(60, 91);
                task.ExecuteAt = DateTime.Now.AddMinutes(skipCycle);
                logger.Information($"Nenhum alvo válido encontrado. Varredura dormindo por {skipCycle} min.");
                taskManager.Add(task);
                return Result.Ok();
            }

            // Ordenação: Herói primeiro e Maior EXP primeiro
            plannedAttacks = plannedAttacks
                .OrderByDescending(p => p.SendHero)
                .ThenByDescending(p => p.TotalExp)
                .ToList();

            bool isHeroAvailable = false;
            if (plannedAttacks.Any(p => p.SendHero))
            {
                var heroStatus = browser.Html.DocumentNode.Descendants("div").FirstOrDefault(x => x.HasClass("heroStatus"));
                isHeroAvailable = heroStatus != null && heroStatus.Descendants("i").Any(x => x.HasClass("heroHome"));
            }

            logger.Information($"--- FASE 3: Disparando {plannedAttacks.Count} ataques ---");

            bool heroAlreadySentInThisRound = false;

            // HashSet para guardar apenas os oásis que foram pulados por falta do herói
            var pendingHeroCoords = new HashSet<(int, int)>();

            foreach (var plan in plannedAttacks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (plan.SendHero)
                {
                    if (!isHeroAvailable || heroAlreadySentInThisRound)
                    {
                        logger.Warning($"Pulando ({plan.X}|{plan.Y}). Exige Herói, mas ele está ocupado ou ausente.");
                        pendingHeroCoords.Add((plan.X, plan.Y)); // Anota este oásis para a fila acelerada!
                        continue;
                    }
                }

                await browser.ExecuteJsScript("window.location.href = '/build.php?id=39&tt=2&gid=16';");
                await System.Threading.Tasks.Task.Delay(1800, cancellationToken);

                string jsFillForm = $$"""
                    function setInputValue(selector, value) {
                        let el = document.querySelector(selector);
                        if (el && !el.disabled) {
                            el.value = value;
                            el.dispatchEvent(new Event('input', { bubbles: true }));
                            el.dispatchEvent(new Event('change', { bubbles: true }));
                        }
                    }
                    setInputValue('#xCoordInput', '{{plan.X}}');
                    setInputValue('#yCoordInput', '{{plan.Y}}');
                    {{(plan.AmountToSend > 0 ? $"setInputValue('input[name=\"troop[t{plan.TroopIndexToUse}]\"]', '{plan.AmountToSend}');" : "")}}
                    {{(plan.SendHero ? "setInputValue('input[name=\"troop[t11]\"]', '1');" : "")}}
                    let raidRadio = document.querySelector('input[name="eventType"][value="4"]');
                    if (raidRadio) raidRadio.click();
                    setTimeout(() => { document.getElementById('ok').click(); }, 500);
                """;

                await browser.ExecuteJsScript(jsFillForm);
                await System.Threading.Tasks.Task.Delay(2800, cancellationToken);

                string htmlConfirm = browser.Html.DocumentNode.OuterHtml;
                if (htmlConfirm.Contains("error") || htmlConfirm.Contains("few troops"))
                {
                    logger.Warning($"❌ Erro na tela de confirmação para ({plan.X}|{plan.Y}). Provável falta de tropas.");
                    continue;
                }

                string jsConfirm = """
                    let btnConfirm = document.getElementById('confirmSendTroops');
                    if (btnConfirm) {
                        btnConfirm.click();
                    } else {
                        let altBtn = document.querySelector('button.rallyPointConfirm');
                        if (altBtn) altBtn.click();
                    }
                """;

                await browser.ExecuteJsScript(jsConfirm);
                logger.Information($"🚀 Ataque enviado para ({plan.X}|{plan.Y})!");

                if (plan.SendHero)
                {
                    heroAlreadySentInThisRound = true;
                }

                await System.Threading.Tasks.Task.Delay(rnd.Next(5000, 10000), cancellationToken);
            }

            // ============================================================
            // FASE 4: DIVISÃO DE ESQUADRÕES (SPLIT TASK)
            // ============================================================

            // Separa os oásis da lista original em dois grupos distintos
            var heroPendingTargets = task.Targets.Where(t => pendingHeroCoords.Contains((t.X, t.Y))).ToList();
            var normalTargets = task.Targets.Where(t => !pendingHeroCoords.Contains((t.X, t.Y))).ToList();

            if (normalTargets.Count > 0)
            {
                int normalCycle = rnd.Next(60, 91);
                var normalTask = new AttackOasisTask.Task(task.AccountId, task.VillageId, normalTargets, task.TribeInt)
                {
                    ExecuteAt = DateTime.Now.AddMinutes(normalCycle)
                };
                taskManager.Add(normalTask);
                logger.Information($"✅ Agendados {normalTargets.Count} oásis para o ciclo normal de {normalCycle} min.");
            }

            if (heroPendingTargets.Count > 0)
            {
                int alertCycle = rnd.Next(15, 26);
                var heroTask = new AttackOasisTask.Task(task.AccountId, task.VillageId, heroPendingTargets, task.TribeInt)
                {
                    ExecuteAt = DateTime.Now.AddMinutes(alertCycle)
                };
                taskManager.Add(heroTask);
                logger.Information($"⏳ MODO ALERTA: {heroPendingTargets.Count} oásis gordos aguardando o Herói. Escolta reagendada para {alertCycle} min.");
            }

            // Retorna Ok() sem reagendar a tarefa raiz original (ela morre aqui e as duas novas assumem).
            return Result.Ok();
        }
    }
}
