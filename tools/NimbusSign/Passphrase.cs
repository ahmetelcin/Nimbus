using System.Text;

internal static class Passphrase
{
    private const int MinLength = 12;

    public static string AskNew()
    {
        var passphrase = Read("New passphrase: ");
        if (passphrase.Length < MinLength)
            throw new KeyFileException($"The passphrase must have at least {MinLength} characters.");
        if (passphrase != Read("Repeat passphrase: "))
            throw new KeyFileException("The passphrases don't match.");
        return passphrase;
    }

    public static string Ask() => Read("Passphrase: ");

    // Nothing is echoed, not even asterisks, so the length stays hidden too.
    private static string Read(string prompt)
    {
        Console.Error.Write(prompt);

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine();
            return Console.In.ReadLine()
                ?? throw new KeyFileException("No passphrase was entered. Run this command in your own terminal window.");
        }

        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length > 0)
                    text.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return text.ToString();
    }
}
