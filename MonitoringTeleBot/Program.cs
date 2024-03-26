// See https://aka.ms/new-console-template for more information
using MonitoringTeleBot;


Console.WriteLine("Hello, i`m is Telegramm Bot Monitoring!!!");
MySQL mySQL = new MySQL();
while (true)
{
    try
    {
        TeleBot teleBot = new TeleBot(mySQL);
        teleBot.Start();
    }
    catch(Exception ex) {  Console.WriteLine(ex.ToString()); }
}
Console.WriteLine("Sorry, I'm tired. I want to sleep.");