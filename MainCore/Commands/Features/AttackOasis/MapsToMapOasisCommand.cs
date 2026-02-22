using FluentResults;
using MainCore.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MainCore.Commands.Features.AttackOasis
{
    [Handler]
    public static partial class NavigateToMapOasisCommand
    {
        // 1. O registro do comando (os dados que ele recebe)
        public record Command(int X, int Y);

        // 2. A função estática que o robô invisível vai usar para criar o Handler
        private static async ValueTask<Result> HandleAsync(
            Command command,
            IChromeBrowser browser, // O gerador de código injeta o navegador automaticamente aqui!
            CancellationToken cancellationToken)
        {
            // Pegamos a URL atual onde o bot já está conectado
            var currentUri = new Uri(browser.CurrentUrl);

            // Extraímos apenas o servidor (ex: https://ts1.x1.europe.travian.com)
            var baseUrl = $"{currentUri.Scheme}://{currentUri.Host}";

            // Montamos a URL absoluta e enviamos o X e o Y
            var mapUrl = $"{baseUrl}/karte.php?x={command.X}&y={command.Y}";

            // Mandamos o navegador ir direto para lá
            await browser.Navigate(mapUrl, cancellationToken);

            // Um breve respiro para garantir que o mapa carregou no Chrome
            await Task.Delay(1500, cancellationToken);

            return Result.Ok();
        }
    }
}
