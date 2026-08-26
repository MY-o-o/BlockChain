using Spectre.Console;

namespace BlockChain.Services
{
    public static class UIUXService
    {
        public static void CustomPrint(string message, string caption = "", Color? color = null)
        {
            color ??= Color.Gray;

            int horizontalAlignment = 0;
            if (caption.Length > message.Length)
            {
                horizontalAlignment = (caption.Length - message.Length) / 2;
            }

            var panel = new Panel($"[bold {color.Value}]{message}[/]")
                .Header($"[{color.Value}]{caption}[/]")
                .BorderColor(color ?? Color.Gray)
                .Border(BoxBorder.Rounded)
                .Padding(2 + horizontalAlignment, 0);
            AnsiConsole.Write(panel);
        }
        public static void ErrorPrint(string message, string caption = "Error") => CustomPrint(message, caption, Color.Red);
        public static void SuccessPrint(string message, string caption = "Success") => CustomPrint(message, caption, Color.Green);
        public static void WarningPrint(string message, string caption = "Warning") => CustomPrint(message, caption, Color.Yellow);
        public static void InfoPrint(string message, string caption = "Info") => CustomPrint(message, caption, Color.SteelBlue1_1);

        public static async Task AwaitingInput(
            string message = "Press any key to continue",
            Style? style = null,
            int delayMs = 800,
            bool isInPanel = false,
            bool isCancelable = true,
            CancellationTokenSource? cts = null)
        {
            style ??= new Style(foreground: Color.Gray, decoration: Decoration.Bold);
            cts ??= new CancellationTokenSource();
            CancellationToken token = cts.Token;
            short dotCounter = 1;

            Console.Write(Environment.NewLine);

            var messageMarkup = new Markup(message, style);
            var panel = new Panel(messageMarkup)
                .BorderColor(style.Value.Foreground)
                .Border(BoxBorder.Rounded)
                .Padding(2, 0);

            Task dotTask = AnsiConsole.Live(isInPanel ? panel : messageMarkup)
                .StartAsync(async ctx =>
                {
                    while (true)
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            var newMessageMarkup = new Markup(message + new string('.', dotCounter) + new string(' ', 3 - dotCounter), style);
                            var newPanel = new Panel(newMessageMarkup)
                                .BorderColor(style.Value.Foreground)
                                .Border(BoxBorder.Rounded)
                                .Padding(2, 0);
                            ctx.UpdateTarget(isInPanel ? newPanel : newMessageMarkup);

                            dotCounter = (short)((dotCounter + 1) % 4);
                            await Task.Delay(delayMs, token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                });

            if (isCancelable)
            {
                await Task.Run(() => {
                    Console.ReadKey(intercept: true);
                    cts.Cancel();
                });
            }

            try
            {
                await dotTask;
            }
            catch (Exception ex)
            {
                ErrorPrint(ex.Message);
            }

            AnsiConsole.Clear();
        }
    }
}
