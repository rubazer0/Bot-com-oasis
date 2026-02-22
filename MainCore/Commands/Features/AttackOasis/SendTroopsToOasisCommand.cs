using FluentResults;
using MainCore.Services;
using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MainCore.Commands.Features.AttackOasis
{
    [Handler]
    public static partial class SendTroopsToOasisCommand
    {
        public record Command(Dictionary<int, int> Troops);

        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser,
            CancellationToken cancellationToken)
        {
            // 1. Clicar no link de ataque do mapa
            var raidLinkResult = await browser.GetElement(By.XPath("//a[contains(@href, 'tt=2') and contains(@href, 'targetMapId')]"), cancellationToken);
            if (raidLinkResult.IsFailed) return Result.Fail("Link 'Raid' não encontrado no mapa.");

            await browser.Click(raidLinkResult.Value, cancellationToken);
            await Task.Delay(3000, cancellationToken);

            // 2. Preencher tropas (Tela 1)
            foreach (var troop in command.Troops)
            {
                var troopInputName = $"troop[t{troop.Key}]";
                var troopResult = await browser.GetElement(By.XPath($"//input[@name='{troopInputName}']"), cancellationToken);

                if (troopResult.IsSuccess)
                {
                    var troopElement = troopResult.Value;
                    troopElement.Clear();
                    troopElement.SendKeys(troop.Value.ToString());
                }
            }

            // 3. Garantir Raid (Assalto)
            var raidRadioResult = await browser.GetElement(By.XPath("//input[@name='eventType' and @value='4']"), cancellationToken);
            if (raidRadioResult.IsSuccess) await browser.Click(raidRadioResult.Value, cancellationToken);

            // 4. Clique em Send (Ir para confirmação)
            var submitBtnResult = await browser.GetElement(By.XPath("//button[@id='ok']"), cancellationToken);
            if (submitBtnResult.IsFailed) return Result.Fail("Botão 'Send' não encontrado.");

            await browser.Click(submitBtnResult.Value, cancellationToken);

            // Espera generosa para carregar a página de confirmação final
            await Task.Delay(4000, cancellationToken);

            // ============================================================
            // 5. VALIDAÇÃO DE SEGURANÇA (MELHORADA)
            // ============================================================
            if (browser.Driver != null)
            {
                // XPath focado no segundo tbody que contém os números
                var xpathUnits = "//table[contains(@class, 'troop_details')]//tbody[@class='units'][2]//td";
                var unitCells = browser.Driver.FindElements(By.XPath(xpathUnits));

                if (unitCells != null && unitCells.Count > 0)
                {
                    foreach (var troop in command.Troops)
                    {
                        int index = troop.Key - 1;
                        if (index < unitCells.Count)
                        {
                            string text = unitCells[index].Text.Trim();
                            // Filtra apenas números para evitar erros com textos ocultos
                            var numericText = new string(text.Where(char.IsDigit).ToArray());
                            int.TryParse(numericText, out int qtyInGame);

                            if (qtyInGame != troop.Value)
                            {
                                return Result.Fail($"SEGURANÇA: Tropas no jogo ({qtyInGame}) diferentes do esperado ({troop.Value}).");
                            }
                        }
                    }
                }
            }

            // ============================================================
            // 6. CLIQUE FINAL (RESILIENTE A TIMEOUT)
            // ============================================================
            try
            {
                // Em vez de usar o GetElement padrão que está dando timeout, usamos uma busca direta e rápida
                var confirmBtn = browser.Driver?.FindElement(By.XPath("//button[@id='confirmSendTroops']"));

                if (confirmBtn != null && confirmBtn.Displayed)
                {
                    await browser.Click(confirmBtn, cancellationToken);
                    return Result.Ok();
                }
                return Result.Fail("Página de confirmação carregou, mas o botão 'Confirm' não apareceu.");
            }
            catch (Exception)
            {
                return Result.Fail("Erro ao tentar clicar no botão de confirmação final.");
            }
        }
    }
}
