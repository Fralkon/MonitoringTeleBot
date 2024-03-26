using System.Data;
using System.Net.NetworkInformation;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MonitoringTeleBot
{
    public class TeleBot
    {
        public static string GetShortPathFile(string path)
        {
            if (path.Length > 15)
                return path.Remove(0, 15);
            else return "";
        }
        public static string GetFullPathFile(string path)
        {
            return "C:\\ZabbixMTBot\\" + path;
        }
        static public bool Ping(string ip)
        {
            Ping ping = new Ping();
            PingReply reply = ping.Send(ip);
            return reply.Status == IPStatus.Success ? true : false;
        }
#if DEBUG
        const string APIHEX = "1607643022:AAGen7PfJb_EhTByGZk4cqooDCGCukcNG4w";
#else
            const string APIHEX = "5653706523:AAEaDXSzaqZ6tmtgHTW76F6IOPRJzFnV6Rg";
#endif
        public MySQL mySQL;
        TelegramBotClient botClient;
        CancellationTokenSource cts;
        UpdateHandler handler;
        ReceiverOptions receiverOptions;
        Timer workDay;
        Timer timerSendMonitoring;
        public TeleBot(MySQL mySQL)
        {
            this.mySQL = mySQL;
            botClient = new TelegramBotClient(APIHEX);
            cts = new CancellationTokenSource();
            handler = new UpdateHandler(this);
            receiverOptions = new ReceiverOptions();           
        }
        ~TeleBot()
        {
            workDay.Dispose();
            cts.Cancel();
            timerSendMonitoring.Dispose();
        }
        public void CheckConnect()
        {
            Console.WriteLine("Wait connection TelegramBot");
            while (true)
            {
                if (Ping("api.telegram.org"))
                {
                    Console.WriteLine("Connected Api Telegram");
                    break;
                }
                else
                {
                    Console.WriteLine("Not connected. \nWait 10 sec.");
                    Thread.Sleep(10000);
                }
            }
        }
        public void Start()
        {
            CheckConnect();
            botClient.StartReceiving(handler, receiverOptions, cancellationToken: cts.Token);
            Console.WriteLine("Bot started");
            if (DateTime.Now.Hour < 9)
                workDay = new Timer(CreateTimerToDay, null,
                DateTime.Now.Date.Add(new TimeSpan(9, 0, 0)) - DateTime.Now, TimeSpan.FromDays(1));
            else
            {
                workDay = new Timer(CreateTimerToDay, null, DateTime.Now.Date.AddDays(1).Add(new TimeSpan(9, 0, 0)) - DateTime.Now, TimeSpan.FromDays(1));
                timerSendMonitoring = new Timer(SendMonitor, null,
                DateTime.Now.Minute < 30 ? DateTime.Now.Date.Add(new TimeSpan(DateTime.Now.Hour, 30, 0)) -
                DateTime.Now : DateTime.Now.Date.Add(new TimeSpan(DateTime.Now.Hour + 1, 0, 0)) - DateTime.Now,
                TimeSpan.FromMinutes(30));
            }
            Task.Delay(-1, cancellationToken: cts.Token).Wait(); // Такой вариант советуют MS: https://github.com/dotnet/runtime/issues/28510#issuecomment-458139641
            Console.WriteLine("Bot stopped");
        }
        public void CreateTimerToDay(object? o)
        {
            SendDeactivateEq();
            timerSendMonitoring = new Timer(SendMonitor, null, TimeSpan.Zero, TimeSpan.FromMinutes(30));
        }
        private void SendMonitor(object? o)
        {
            if (DateTime.Now.Hour < 18 && DateTime.Now.Hour >= 8)
            {
                Console.WriteLine("Send monitoring");
                DataTable users = mySQL.GetDataTableSQL("SELECT id_chat, branchs FROM monitoring_user WHERE monitoring = 1");
                foreach (DataRow user in users.Rows)
                {
                    Console.WriteLine("User send: " + user["id_chat"]);
                    ChatId chat = new ChatId(long.Parse(user["id_chat"].ToString()));
                    SendMonitoring(chat, user["branchs"].ToString());
                }
            }
            else
            {
                timerSendMonitoring.Dispose();
            }
        }
        private void SendDeactivateEq()
        {
            using (DataTable users = mySQL.GetDataTableSQL("SELECT id_chat, branchs FROM monitoring_user WHERE monitoring = 1"))
            {
                foreach (DataRow user in users.Rows)
                {
                    ChatId chat = new ChatId(long.Parse(user["id_chat"].ToString()));
                    using (DataTable equipment = mySQL.GetDataTableSQL("SELECT monitoring_branch.name, monitoring_equipment.name, monitoring_equipment_type.type, ip FROM monitoring_equipment,monitoring_equipment_type, monitoring_branch WHERE monitoring = 0 AND branch_id IN(" + user["branchs"].ToString() + ") AND monitoring_equipment.type_id = monitoring_equipment_type.id AND monitoring_equipment.branch_id = monitoring_branch.id"))
                    {
                        if (equipment.Rows.Count != 0)
                        {
                            string text = "Неактивированое оборудование : \n\n";
                            foreach (DataRow row in equipment.Rows)
                            {
                                text += "Филиал : " + row[0] +
                                        "\nТип : " + row[2] +
                                        "\nНаименование : " + row[1].ToString() +
                                        "\nIP : <code>" + row["ip"] +
                                        "</code>\n\n";
                            }
                            botClient.SendTextMessageAsync(chatId: chat,
                                             text: text,
                                             parseMode: ParseMode.Html);
                        }
                    }
                    SendMonitoring(chat, user["branchs"].ToString());
                }
            }
        }
        public async void SendMonitoring(ChatId chatID, string branchs_text)
        {
            DataTable dataTable = mySQL.GetDataTableSQL("SELECT monitoring_branch.name, monitoring_equipment.name, ip, monitoring_equipment_type.type, time_off FROM monitoring_equipment, monitoring_equipment_type, monitoring_branch WHERE branch_id IN(" + branchs_text + ")" +
                " AND monitoring = 1 AND status = 0 AND type_id = monitoring_equipment_type.id AND branch_id = monitoring_branch.id ORDER BY time_off");
            try
            {
                if (dataTable.Rows.Count > 0)
                {
                    string message = "";
                    foreach (DataRow row in dataTable.Rows)
                    {
                        TimeSpan t = DateTime.Now - DateTime.Parse(row["time_off"].ToString());
                        message += "<b>" + row[0] + "</b> <u>" +
                            row["type"] + " " +
                            row[1] + "</u> <b>OFF: " +
                            (t.TotalDays >= 1.0 ? t.ToString(@"dd\ hh\:mm\:ss") : t.ToString(@"hh\:mm\:ss")) + "</b>\n<code>" +
                            row["ip"] + "</code>\n";
                    }
                    await botClient.SendTextMessageAsync(chatID, message, parseMode: ParseMode.Html);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatID, "Все в сети");
                }
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        public void SendMonitoring(ChatId chatID)
        {
            DataTable user = mySQL.GetDataTableSQL("SELECT branchs FROM monitoring_user WHERE id_chat = " + chatID.Identifier);
            SendMonitoring(chatID, user.Rows[0]["branchs"].ToString());
        }
        public void SendGeneralMessage(string text)
        {
            DataTable users = mySQL.GetDataTableSQL("SELECT id_chat FROM monitoring_user");
            foreach (DataRow user in users.Rows)
            {
                ChatId chat = new ChatId(long.Parse(user["id_chat"].ToString()));
                botClient.SendTextMessageAsync(chat, text, parseMode: ParseMode.Html);
            }
        }
        class UpdateHandler : IUpdateHandler
        {
            TeleBot bot;
            public UpdateHandler(TeleBot teleBot)
            {
                bot = teleBot;
            }
            public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
            {
                if (update.Type == UpdateType.Message)
                {
                    var message = update.Message;
                    if (!CheckChatIdBD(message.Chat.Id))
                    {
                        string meggage = "Мой имя : " + message.Chat.FirstName + " " + message.Chat.LastName + "\n" +
                                              "Мой ID : " + message.Chat.Id;
                        await botClient.SendTextMessageAsync(message.Chat, "Вы не добавлены в базу данных, обратитесь к администратору.\n" + meggage);
                        return;
                    }
                    try
                    {
                        Action(message.Text.Split(' '), botClient, update.Message.Chat, update, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    bot.mySQL.SendSQL("UPDATE monitoring_user SET col_sq = col_sq + 1 WHERE id_chat = " + update.Message.Chat.Id.ToString());
                }
                else if (update.Type == UpdateType.CallbackQuery)
                {
                    try
                    {
                        Action(update.CallbackQuery.Data.Split(' '), botClient, update.CallbackQuery.Message.Chat, update, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            private async void Action(string[] actions, ITelegramBotClient botClient, Chat chat, Update update, CancellationToken cancellationToken)
            {
                try
                {
                    Console.WriteLine(string.Join(" ", actions));
                    string mainAction = actions[0][0] == '/' ? actions[0].Substring(1).ToLower() : actions[0].ToLower();
                    switch (mainAction)
                    {
                        case "info":
                            {
                                if (actions.Length == 1)
                                {

                                    DataTable monitoring_info = bot.mySQL.GetDataTableSQL("SELECT DISTINCT type FROM monitoring_info ORDER BY type");
                                    List<InlineKeyboardButton[]> buttons = new List<InlineKeyboardButton[]>();
                                    foreach (DataRow row in monitoring_info.Rows)
                                    {
                                        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(text: row["type"].ToString(), callbackData: "info " + row["type"].ToString()) });
                                    }
                                    InlineKeyboardMarkup inlineKeyboard = new InlineKeyboardMarkup(buttons);
                                    await botClient.SendTextMessageAsync(
                                        chatId: chat,
                                        text: "info",
                                        replyMarkup: inlineKeyboard,
                                        cancellationToken: cancellationToken);
                                }
                                else if (IsIP(actions[1]))
                                {
                                    using (DataTable equipmentData = bot.mySQL.GetDataTableSQL("SELECT monitoring_equipment.id, monitoring_branch.name, monitoring_equipment.name, monitoring_equipment_type.type, status, monitoring, ip FROM monitoring_equipment, monitoring_equipment_type, monitoring_branch WHERE ip = '" + actions[1] + "' AND type_id = monitoring_equipment_type.id AND branch_id = monitoring_branch.id"))
                                    {
                                        if (equipmentData.Rows.Count != 0)
                                        {
                                            DataRow equipment = equipmentData.Rows[0];
                                            InlineKeyboardMarkup inlineKeyboard = new InlineKeyboardMarkup(
                                                InlineKeyboardButton.WithCallbackData(text: (equipment["monitoring"].ToString() == "True" ? "Деактивировать" : "Активировать"), callbackData: "monitoring " +
                                                equipment["id"].ToString() + " " + (equipment["monitoring"].ToString() == "True" ? "deactivate" : "activate"))
                                                );
                                            await botClient.SendTextMessageAsync(chatId: chat,
                                                          text: "Филиал : " + equipment[1].ToString() +
                                                                "\nТип : " + equipment["type"].ToString() +
                                                                "\nНаименование : " + equipment[2].ToString() +
                                                                "\nIP : <code>" + equipment["ip"] +
                                                                "</code>\nМониторинг : " + (equipment["monitoring"].ToString() == "True" ? "Включен" : "Отключен") +
                                                                "\nСтатус : " + (equipment["status"].ToString() == "True" ? "В сети" : "Не в сети"),
                                                          parseMode: ParseMode.Html,
                                                          replyMarkup: inlineKeyboard,
                                                          cancellationToken: cancellationToken);
                                        }
                                        else
                                        {
                                            await botClient.SendTextMessageAsync(chatId: chat,
                                                          text: "Данный IP не найден",
                                                          parseMode: ParseMode.Html,
                                                          cancellationToken: cancellationToken);
                                        }
                                    }
                                }
                                else
                                {
                                    DataTable monitoring_infoType = bot.mySQL.GetDataTableSQL("SELECT DISTINCT type FROM monitoring_info");
                                    foreach (DataRow monitoring_info in monitoring_infoType.Rows)
                                    {
                                        if (actions[1] == monitoring_info[0].ToString())
                                        {
                                            if (actions.Length == 2)
                                            {
                                                DataTable monitoring_infoTopic = bot.mySQL.GetDataTableSQL("SELECT topic FROM monitoring_info WHERE type = '" + actions[1] + "' ORDER BY topic");
                                                List<InlineKeyboardButton[]> buttons = new List<InlineKeyboardButton[]>();
                                                foreach (DataRow topic in monitoring_infoTopic.Rows)
                                                {
                                                    buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(text: topic[0].ToString(), callbackData: "info " + actions[1] + " " + topic[0].ToString()) });
                                                }
                                                InlineKeyboardMarkup inlineKeyboard = new InlineKeyboardMarkup(buttons);

                                                await botClient.SendTextMessageAsync(
                                                     chatId: chat,
                                                     text: actions[1],
                                                     replyMarkup: inlineKeyboard,
                                                     cancellationToken: cancellationToken);
                                            }
                                            else if (actions.Length == 3)
                                            {
                                                DataTable monitoring_infomonitoring_info = bot.mySQL.GetDataTableSQL("SELECT info, file FROM monitoring_info WHERE type = '" + actions[1] + "' AND topic = '" + actions[2] + "'");
                                                if (monitoring_infomonitoring_info.Rows[0]["file"].ToString() != "")
                                                {
                                                    FileStream file = System.IO.File.OpenRead(GetFullPathFile(monitoring_infomonitoring_info.Rows[0]["file"].ToString()));
                                                    string typeFile = Path.GetExtension(file.Name);
                                                    if (typeFile == ".jpg" || typeFile == ".png" || typeFile == ".jpeg")
                                                        await botClient.SendPhotoAsync(
                                                            chatId: chat,
                                                            photo: new InputFileStream(file, "document" + typeFile),
                                                            caption: monitoring_infomonitoring_info.Rows[0][0].ToString(),
                                                            cancellationToken: cancellationToken
                                                        );
                                                    else
                                                        await botClient.SendDocumentAsync(
                                                        chatId: chat,
                                                        document: new InputFileStream(file, "document" + typeFile),
                                                        caption: monitoring_infomonitoring_info.Rows[0][0].ToString(),
                                                        cancellationToken: cancellationToken
                                                    );
                                                }
                                                else
                                                {
                                                    await botClient.SendTextMessageAsync(
                                                    chatId: chat,
                                                    text: monitoring_infomonitoring_info.Rows[0][0].ToString(),
                                                    cancellationToken: cancellationToken);
                                                }
                                            }

                                            return;
                                        }
                                    }
                                    await botClient.SendTextMessageAsync(
                                                    chatId: chat,
                                                    text: "Ошибка запроса",
                                                    cancellationToken: cancellationToken);
                                }
                                break;
                            }
                        //case "equipment":
                        //    {
                        //        if (actions.Length > 1)
                        //        {
                        //            switch (actions[1])
                        //            {
                        //                case "info":
                        //                    {
                        //                        string type = actions.Length == 5 ? actions[3] + "~" + actions[4] : actions[3];
                        //                        await botClient.SendTextMessageAsync(chatId: update.CallbackQuery.Message.Chat.Id,
                        //                                        text: "Тип оборудования : " + type,
                        //                                        parseMode: ParseMode.Html,
                        //                                        cancellationToken: cancellationToken);
                        //                        using (DataTable eqType = bot.mySQL.GetDataTableSQL("SELECT id, name, ip, status, monitoring FROM monitoring_equipment WHERE branch = '" + actions[2] + "' AND type = '" + type + "' AND type_id = monitoring_equipment_type.id AND branch_id = monitoring_branch.id ORDER BY monitoring"))
                        //                        {

                        //                            foreach (DataRow equip in eqType.Rows)
                        //                            {
                        //                                InlineKeyboardMarkup inlineKeyboard = new InlineKeyboardMarkup(
                        //                                    InlineKeyboardButton.WithCallbackData(text: (equip["monitoring"].ToString() == "True" ? "Деактивировать" : "Активировать"), callbackData: "monitoring~" + equip["id"].ToString() + "~" + (equip["monitoring"].ToString() == "True" ? "deactivate" : "activate"))
                        //                                    );
                        //                                await botClient.SendTextMessageAsync(chatId: chat,
                        //                                              text: "Наименование : " + equip["name"].ToString() +
                        //                                                    "\nIP : <code>" + equip["ip"] +
                        //                                                    "</code>\nМониторинг : " + (equip["monitoring"].ToString() == "True" ? "Включен" : "Отключен") +
                        //                                                    "\nСтатус : " + (equip["status"].ToString() == "True" ? "В сети" : "Не в сети"),
                        //                                              parseMode: ParseMode.Html,
                        //                                              replyMarkup: inlineKeyboard,
                        //                                              cancellationToken: cancellationToken);
                        //                            }
                        //                        }
                        //                        break;
                        //                    }
                        //            }
                        //        }
                        //        break;
                        //    }
                        case "ping":
                            {
                                if (actions.Length == 2)
                                {
                                    try
                                    {
                                        for (int i = 0; i < 4; i++)
                                        {
                                            if (Ping(actions[1]))
                                                await botClient.SendTextMessageAsync(chat, (i + 1).ToString() + "/4 ping : <code>" + actions[1] + "</code> OK", parseMode: ParseMode.Html);
                                            else
                                                await botClient.SendTextMessageAsync(chat, (i + 1).ToString() + "/4 ping : <code>" + actions[1] + "</code> ERROR", parseMode: ParseMode.Html);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(ex.ToString());
                                        await botClient.SendTextMessageAsync(chat, "Ошибка запроса");
                                    }
                                }
                                else
                                {
                                    await botClient.SendTextMessageAsync(chat, "Вы не указали IP. Пример: /ping 8.8.8.8");
                                }
                                break;
                            }
                        case "start":
                            {
                                string mes = "Мой имя : " + chat.FirstName + " " + chat.LastName + "\n" +
                                    "Мой ID : " + chat.Id;
                                DataTable userData = bot.mySQL.GetDataTableSQL("SELECT * FROM monitoring_user WHERE id_chat = " + chat.Id.ToString());
                                mes += "\nФилиалы : " + userData.Rows[0]["branchs"].ToString();
                                await botClient.SendTextMessageAsync(chat, mes);
                                break;
                            }
                        case "check":
                            {
                                if (actions.Length == 1)
                                    bot.SendMonitoring(chat);
                                else if (actions.Length == 2)
                                    bot.SendMonitoring(chat, actions[1]);
                                else
                                    await botClient.SendTextMessageAsync(chat, "Не верный запрос");
                                break;
                            }
                        case "help":
                            {
                                string text =
                                    "Что я могу?!\n" +
                                    "Отвечать на ваши запросы." +
                                    "Есть несколько типов запроса, короткие и длиные.\n" +
                                    "Короткие:\n" +
                                    "<u>/start</u> - информация о пользователе\n\n" +
                                    "<u>/check</u> - проверка доступности оборудования на прикрепленых филлиалах\n\n" +
                                    "<u>/monitoring_info</u> - некоторая полезная информация\n\n" +
                                    "<u>/monitoring</u> - список неактивированого в мониторинге оборудования\n\n" +
                                    "<u>'Наименование филиала'</u> (ASF1) - информация по филиалу\n\n" +
                                    "Длиные запросы:\n" +
                                    "<u>/ping</u> 'ip' (ping <code>8.8.8.8</code>) - ping на определенный IP (так же можно просто написать IP оборудования)\n\n" +
                                    "<u>/check</u> 'название филиала через запятую' (<u>check astf3,astf4</u>) - проверка доступности оборудования на определенных филиалах\n\n" +
                                    "<u>/monitoring_info</u> 'наименование оборудование' 'тема' (<u>monitoring_info 5200j net</u>) - полезная информация\n" +
                                    "<u>/monitoring_info 'ip'</u> (monitoring_info <code>8.8.8.8</code>) - информация по оборудованию)\n\n\nА что можешь ты?!!";
                                await botClient.SendTextMessageAsync(chat, text, parseMode: ParseMode.Html);
                                break;
                            }
                        //case "monitoring":
                        //    {
                        //        if (actions.Length == 3)
                        //        {
                        //            bot.mySQL.SendSQL("UPDATE monitoring_equipment SET monitoring = '" + (actions[2] == "activate" ? 1 : 0).ToString() +
                        //                                "' WHERE id = " + actions[1]);
                        //            await botClient.SendTextMessageAsync(chatId: chat,
                        //                              text: "В мониторинге " + (actions[2] == "activate" ? "активирован" : "деактивирован"),
                        //                              parseMode: ParseMode.Html,
                        //                              cancellationToken: cancellationToken);
                        //        }
                        //        else
                        //        {
                        //            string branchsSQL = "";
                        //            using (DataTable user = bot.mySQL.GetDataTableSQL("SELECT branchs FROM monitoring_user WHERE id_chat = " + chat.Id.ToString()))
                        //            {
                        //                string[] branchs = user.Rows[0]["branchs"].ToString().Split(',');
                        //                foreach (string branch in branchs)
                        //                {
                        //                    branchsSQL += "'" + branch + "',";
                        //                }
                        //            }
                        //            using (DataTable equipment = bot.mySQL.GetDataTableSQL("SELECT id, branch, name, ip, type, monitoring FROM monitoring_equipment WHERE (monitoring = 0) AND (branch IN(" + branchsSQL.Substring(0, branchsSQL.Length - 1) + "))"))
                        //            {
                        //                foreach (DataRow row in equipment.Rows)
                        //                {
                        //                    InlineKeyboardMarkup inlineKeyboard = new InlineKeyboardMarkup(
                        //                                InlineKeyboardButton.WithCallbackData(text: (row["monitoring"].ToString() == "True" ? "Деактивировать" : "Активировать"), callbackData: "monitoring " + row["id"].ToString() + " " + (row["monitoring"].ToString() == "True" ? "deactivate" : "activate"))
                        //                                );
                        //                    await botClient.SendTextMessageAsync(chatId: chat,
                        //                                  text: "Филиал : " + row["branch"] +
                        //                                        "\nТип : " + row["type"] +
                        //                                        "\nНаименование : " + row["name"].ToString() +
                        //                                        "\nIP : <code>" + row["ip"] +
                        //                                        "</code>\nМониторинг : " + (row["monitoring"].ToString() == "True" ? "Включен" : "Отключен") + "\n",
                        //                                  parseMode: ParseMode.Html,
                        //                                  replyMarkup: inlineKeyboard,
                        //                                  cancellationToken: cancellationToken);
                        //                }
                        //            }
                        //        }
                        //        break;
                        //    }
                        default:
                            {
                                if (IsIP(mainAction))
                                {
                                    try
                                    {
                                        using (DataTable equipment = bot.mySQL.GetDataTableSQL("SELECT monitoring_branch.name, monitoring_equipment.name, ip,  monitoring_equipment_type.type, monitoring FROM  monitoring_equipment,monitoring_equipment_type, monitoring_branch WHERE ip = '" + mainAction + "' AND type_id = monitoring_equipment_type.id AND branch_id = monitoring_branch.id"))
                                        {
                                            if (equipment.Rows.Count > 0)
                                            {
                                                DataRow row = equipment.Rows[0];
                                                await botClient.SendTextMessageAsync(chatId: chat,
                                                              text: "Филиал : " + row[0] +
                                                                    "\nТип : " + row["type"] +
                                                                    "\nНаименование : " + row[1].ToString() +
                                                                    "\nIP : <code>" + row["ip"] +
                                                                    "</code>\nМониторинг : " + (row["monitoring"].ToString() == "True" ? "Включен" : "Отключен") + "\n",
                                                              parseMode: ParseMode.Html,
                                                              cancellationToken: cancellationToken);
                                                for (int i = 0; i < 4; i++)
                                                {
                                                    if (Ping(mainAction))
                                                        await botClient.SendTextMessageAsync(chat, (i + 1).ToString() + "/4 ping : <code>" + mainAction + "</code> OK", parseMode: ParseMode.Html);
                                                    else
                                                        await botClient.SendTextMessageAsync(chat, (i + 1).ToString() + "/4 ping : <code>" + mainAction + "</code> ERROR", parseMode: ParseMode.Html);
                                                }
                                            }
                                            else
                                            {
                                                await botClient.SendTextMessageAsync(chat, "Данный IP отсутствует в базе");
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine(ex.ToString());
                                        await botClient.SendTextMessageAsync(chat, "Ошибка запроса");
                                    }
                                    return;
                                }
                                else if (IsBranch(mainAction))
                                {
                                    mainAction = mainAction.ToUpper();
                                    if (actions.Length == 1)
                                    {
                                        DataTable userTable = bot.mySQL.GetDataTableSQL("SELECT branchs FROM monitoring_user WHERE id_chat = " + chat.Id.ToString());
                                        string[] splitBranch = userTable.Rows[0]["branchs"].ToString().Split(',');
                                        foreach (string branch in splitBranch)
                                        {
                                            if (branch == mainAction)
                                            {
                                                string mess = "Филиал : " + branch;
                                                DataTable dataEqType = bot.mySQL.GetDataTableSQL("SELECT type FROM monitoring_equipment_type ORDER BY type");
                                                List<InlineKeyboardButton[]> buttons = new List<InlineKeyboardButton[]>();
                                                foreach (DataRow rowEqType in dataEqType.Rows)
                                                {
                                                    buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(text: rowEqType["type"].ToString(), callbackData: "equipment monitoring_info " + branch + " " + rowEqType["type"].ToString()) });
                                                }
                                                InlineKeyboardMarkup inlineKeyboard = new InlineKeyboardMarkup(buttons);
                                                await botClient.SendTextMessageAsync(
                                                    chatId: chat,
                                                    text: mess,
                                                    replyMarkup: inlineKeyboard,
                                                    cancellationToken: cancellationToken);
                                                return;
                                            }
                                        }
                                        await botClient.SendTextMessageAsync(chat, "Не верный филиал");
                                    }
                                }
                                else
                                    await botClient.SendTextMessageAsync(chat, "Не верный запрос");
                                break;
                            }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    await botClient.SendTextMessageAsync(
                              chatId: "Запрос отправлен не верно.",
                              text: "monitoring_info",
                              cancellationToken: cancellationToken);
                }
            }
            private bool IsIP(string ip)
            {
                string[] vs = ip.Split('.');
                if (vs.Length != 4)
                    return false;
                foreach (string v in vs)
                {
                    if (v.Length > 3 || v.Length == 0)
                        return false;
                    if (!int.TryParse(v, out int value))
                        return false;
                }
                return true;
            }
            private bool IsBranch(string branch)
            {
                if (branch.ToUpper().IndexOf("ASF") == -1)
                    return false;
                else
                    return true;
            }
            public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
            {
                Console.Error.WriteLine(exception);
                return Task.CompletedTask;
            }
            public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
            bool CheckChatIdBD(long id)
            {
                using (DataTable users = bot.mySQL.GetDataTableSQL("SELECT id_chat FROM monitoring_user"))
                {
                    foreach (DataRow user in users.Rows)
                    {
                        if (long.Parse(user["id_chat"].ToString()) == id)
                            return true;
                    }
                    return false;
                }
            }
        }
    }
}
