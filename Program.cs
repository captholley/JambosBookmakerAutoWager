using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Globalization;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using PinnacleWrapper;
using PinnacleWrapper.Data;
using PinnacleWrapper.Enums;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using S22.Imap;

namespace JAMBOS_Sports_Bets
{
    class Program
    {
        public static StringBuilder lineString = new StringBuilder();
        public static List<BettingLines> _todaysLines = new List<BettingLines>();
        public static List<BettingLines> _allLines = new List<BettingLines>();
        public static List<BettingLines> _openBets = new List<BettingLines>();
        static AutoResetEvent reconnectEvent = new AutoResetEvent(false);
        static ImapClient client;
        static SmtpClient sendclient;
        public static async Task Main(string[] args)
        {
            Console.WriteLine("Starting up the JAMBOS Sports Betting Automator");

            //_todaysLines = GetTodaysLines();
           // SendTodaysLinesViaText(lineString);
            //PlaceBookmakerBets(_todaysLines);

            try
            {
                while (true)
                {
                    Console.WriteLine("Checking for any Open Bets");
                    _openBets = GetOpenBets();
                    Console.WriteLine("Connecting to GMail...");
                    InitializeClient();
                    Console.WriteLine("Connection Successful. Waiting for Message");
                    reconnectEvent.WaitOne();
                }
            }
            finally
            {
                if (client != null)
                    client.Dispose();
            }

        }

        #region JAMBOS Methods
        public static List<BettingLines> GetTodaysLines()
        {
            List<BettingLines> bets = new List<BettingLines>();

            using (var chrome = new ChromeDriver(Environment.CurrentDirectory))
            {
                var userName = "";
                var password = "";
                bool header = true;

                chrome.Url = "https://jambospicks.com/wp-login.php";
                chrome.Navigate();


                // Assumes that the login form contains the following elements:
                // <input name="user">
                // <input name="pass">
                // <input type="submit" value="Submit">
                try
                {
                    chrome.FindElementById("user_login").SendKeys(userName);
                    chrome.FindElementById("user_pass").SendKeys(password);
                    chrome.FindElementById("wp-submit").Click();
                }
                catch
                {
                    Console.WriteLine("Unable to log in to JAMBOS, breaking operation now");
                    throw;
                }

                chrome.Url = "https://jambospicks.com/recommendations/";
                chrome.Navigate();

                IWebElement elemTable = chrome.FindElementById("table_1");

                // Fetch all Row of the table
                List<IWebElement> lstTrElem = new List<IWebElement>(elemTable.FindElements(By.XPath("//*[contains(@id,'table_3_row')]")));

                string strRowData = "";
                string phoneMsg = "| --";

                // Traverse each row
                foreach (var elemTr in lstTrElem)
                {
                    // Fetch the columns from a particuler row
                    List<IWebElement> lstTdElem = new List<IWebElement>(elemTr.FindElements(By.TagName("td")));
                    if (lstTdElem.Count > 0)
                    {
                        // Traverse each column
                        BettingLines temp = new BettingLines();
                        foreach (var elemTd in lstTdElem)
                        {
                            // "\t\t" is used for Tab Space between two Text
                            strRowData = strRowData + elemTd.Text + "\t\t";
                            var t = elemTd.GetAttribute("class");
                            try
                            {
                                if (t.Contains("game_date"))
                                {
                                    temp.GameTime = Convert.ToDateTime(elemTd.Text);
                                    phoneMsg = phoneMsg + elemTd.Text + "   ";
                                }
                                else if (t.Contains("sport_type"))
                                {
                                    temp.League = LeagueConverter(elemTd.Text);
                                    phoneMsg = phoneMsg + elemTd.Text + "   ";
                                }
                                else if (t.Contains("game_time"))
                                {
                                    try
                                    {
                                        //temp.GameTime.AddHours((int)t.Substring(2);
                                    }
                                    catch { }
                                }
                                else if (t.Contains("matchup"))
                                {
                                    int atSign = elemTd.Text.IndexOf("@");
                                    int len = elemTd.Text.Length;
                                    if (temp.League == Leagues.MLB)
                                    {
                                        string h = elemTd.Text.Substring(atSign + 2);
                                        temp.HomeTeam = h.Substring(0, h.IndexOf(" "));
                                        temp.AwayTeam = elemTd.Text.Substring(0, elemTd.Text.IndexOf(" "));
                                    }
                                    else if (temp.League == Leagues.NFL)
                                    {
                                        temp.HomeTeam = elemTd.Text.Substring(atSign + 2, len - (atSign + 2));
                                        temp.AwayTeam = elemTd.Text.Substring(0, atSign - 1);
                                    }
                                    else
                                    {
                                        temp.HomeTeam = elemTd.Text.Substring(atSign + 2, len - (atSign + 2));
                                        temp.AwayTeam = elemTd.Text.Substring(0, atSign - 1);
                                    }
                                    phoneMsg = phoneMsg + temp.AwayTeam + " @ " + temp.HomeTeam + "   ";
                                }
                                else if (t.Contains("play"))
                                {
                                    if (elemTd.Text.Contains(" +") && !elemTd.Text.Contains("(+") && !elemTd.Text.Contains("(-"))
                                    {
                                        int dir = elemTd.Text.LastIndexOf(" +") + 2;
                                        int len = elemTd.Text.Length;
                                        temp.Play = elemTd.Text.Substring(0, dir - 2);
                                        temp.Line = Convert.ToInt32(elemTd.Text.Substring(dir, len - dir));
                                    }
                                    else if (elemTd.Text.Contains(" -") && !elemTd.Text.Contains("(+") && !elemTd.Text.Contains("(-"))
                                    {
                                        int dir = elemTd.Text.LastIndexOf(" -") + 2;
                                        int len = elemTd.Text.Length;
                                        temp.Play = elemTd.Text.Substring(0, dir - 2);
                                        temp.Line = Convert.ToInt32(elemTd.Text.Substring(dir, len - dir)) * -1;
                                    }
                                    else if (elemTd.Text.Contains("(+"))
                                    {
                                        int dir = elemTd.Text.LastIndexOf("(+") + 2;
                                        int len = elemTd.Text.Length - 1;
                                        temp.Play = elemTd.Text.Substring(0, dir - 3);
                                        temp.Line = Convert.ToInt32(elemTd.Text.Substring(dir, len - dir));
                                    }
                                    else if (elemTd.Text.Contains("(-"))
                                    {
                                        int dir = elemTd.Text.LastIndexOf("(-") + 2;
                                        int len = elemTd.Text.Length - 1;
                                        temp.Play = elemTd.Text.Substring(0, dir - 3);
                                        temp.Line = Convert.ToInt32(elemTd.Text.Substring(dir, len - dir));
                                    }
                                    else
                                    {
                                        temp.Play = elemTd.Text;
                                    }



                                    if (temp.Play.Contains("(F5)") || temp.Play.Contains("(1H)"))
                                    {
                                        temp.FirstHalf = true;
                                        temp.Play = temp.Play.Replace("(F5)", "");
                                        temp.Play = temp.Play.Replace("(1H)", "");
                                    }
                                    if (temp.Play.Contains(temp.HomeTeam) && !temp.Play.Contains("+") && !temp.Play.Contains("-"))
                                    {
                                        temp.Bet = PlayType.HomeMoneyLine;
                                        if (temp.League == Leagues.MLB)
                                        {
                                            if (temp.Play.Contains(" "))
                                            {
                                                temp.Play = temp.Play.Replace(temp.Play.Substring(0, temp.Play.IndexOf(" ")), MLBConverter(temp.Play.Substring(0, temp.Play.IndexOf(" ")), true));
                                            }
                                            else
                                            {
                                                temp.Play = MLBConverter(temp.Play, true);
                                            }
                                        }
                                    }
                                    else if (temp.Play.Contains(temp.AwayTeam) && !temp.Play.Contains("+") && !temp.Play.Contains("-"))
                                    {
                                        temp.Bet = PlayType.AwayMoneyLine;
                                        if (temp.League == Leagues.MLB)
                                        {
                                            if (temp.Play.Contains(" "))
                                            {
                                                temp.Play = temp.Play.Replace(temp.Play.Substring(0, temp.Play.IndexOf(" ")), MLBConverter(temp.Play.Substring(0, temp.Play.IndexOf(" ")), true));
                                            }
                                            else
                                            {
                                                temp.Play = MLBConverter(temp.Play, true);
                                            }
                                        }
                                    }
                                    else if (temp.Play.Contains("+") || temp.Play.Contains("-"))
                                    {
                                        if (temp.Play.Contains(temp.HomeTeam))
                                        {
                                            temp.Bet = PlayType.HomeHandicap;
                                        }
                                        else if (temp.Play.Contains(temp.AwayTeam))
                                        {
                                            temp.Bet = PlayType.AwayHandicap;
                                        }
                                        if (temp.Play.Contains("+"))
                                        {
                                            temp.Play = temp.Play.Substring(0, temp.Play.IndexOf("+"));
                                        }
                                        else if (temp.Play.Contains("-"))
                                        {
                                            temp.Play = temp.Play.Substring(0, temp.Play.IndexOf("-"));
                                        }
                                    }
                                    else if (temp.Play.Contains("Over") || temp.Play.Contains("o"))
                                    {
                                        if (temp.Play.Contains("o") && !temp.Play.Contains("Over"))
                                        {
                                            temp.Play = temp.Play.Replace("o", "Over ");
                                        }
                                        temp.Bet = PlayType.OverPointsLine;
                                    }
                                    else if (temp.Play.Contains("Under") || temp.Play.Contains("u"))
                                    {
                                        if (temp.Play.Contains("u") && !temp.Play.Contains("Under"))
                                        {
                                            temp.Play = temp.Play.Replace("u", "Under");
                                        }
                                        temp.Bet = PlayType.UnderPointsLine;
                                    }
                                    phoneMsg = phoneMsg + "//" + elemTd.Text + "\\" + "   ";
                                }
                                else if (t.Contains("release"))
                                {
                                    var n = DateTime.Now.Year.ToString() + "/" + elemTd.Text;
                                    try
                                    {
                                        temp.ReleaseTime = DateTime.ParseExact(n, "yyyy/MM/dd hh:mmtt", null);
                                    }
                                    catch
                                    {
                                        try
                                        {
                                            temp.ReleaseTime = DateTime.ParseExact(n, "yyyy/MM/dd hh:mm tt", null);
                                        }
                                        catch
                                        {
                                            temp.ReleaseTime = temp.GameTime;
                                        }
                                    }
                                    if (temp.ReleaseTime.DayOfYear < DateTime.Now.DayOfYear)
                                    {
                                        //return bets;
                                    }
                                }
                            }
                            catch
                            {
                                Console.WriteLine("Error Recording " + t.ToString());
                            }
                        }
                        if (temp.League == Leagues.NFL)
                        {
                            temp.HomeTeam = NFLConverter(temp.HomeTeam, true);
                            temp.AwayTeam = NFLConverter(temp.AwayTeam, true);
                        }
                        else if (temp.League == Leagues.MLB)
                        {
                            temp.HomeTeam = MLBConverter(temp.HomeTeam, true);
                            temp.AwayTeam = MLBConverter(temp.AwayTeam, true);
                        }
                        header = false;
                        bets.Add(temp);
                    }
                    else
                    {
                        // To print the data into the console
                        Console.WriteLine("This is Header Row");
                        Console.WriteLine(lstTrElem[0].Text.Replace(" ", "\t\t"));
                    }

                    Console.WriteLine(strRowData);
                    phoneMsg += "-- |";
                    lineString.AppendFormat(phoneMsg, Environment.NewLine);
                    strRowData = String.Empty;
                    phoneMsg = "| --";
                }
                Console.WriteLine("");
                
                chrome.Quit();
                return bets;
            }

        }
        //NEEDS EDITING - This table does not have a column for the individual sport. May have to request this.
        public static List<BettingLines> GetAllLines()
        {
            List<BettingLines> bets = new List<BettingLines>();

            using (var chrome = new ChromeDriver(Environment.CurrentDirectory))
            {
                var userName = "";
                var password = "";
                bool header = true;

                chrome.Url = "https://jambospicks.com/wp-login.php";
                chrome.Navigate();


                // Assumes that the login form contains the following elements:
                // <input name="user">
                // <input name="pass">
                // <input type="submit" value="Submit">
                chrome.FindElementById("user_login").SendKeys(userName);
                chrome.FindElementById("user_pass").SendKeys(password);
                chrome.FindElementById("wp-submit").Click();

                chrome.Url = "https://jambospicks.com/4-week-9-24-19/";
                chrome.Navigate();

                chrome.FindElementById("header-1565974056081").Click();
                IWebElement elemTable = chrome.FindElementById("table_3");

                // Fetch all Row of the table
                List<IWebElement> lstTrElem = new List<IWebElement>(elemTable.FindElements(By.XPath("//*[contains(@id,'table_3_row')]")));
                String strRowData = "";

                // Traverse each row
                foreach (var elemTr in lstTrElem)
                {
                    // Fetch the columns from a particuler row
                    List<IWebElement> lstTdElem = new List<IWebElement>(elemTr.FindElements(By.TagName("td")));
                    if (lstTdElem.Count > 0)
                    {
                        // Traverse each column
                        BettingLines temp = new BettingLines();
                        foreach (var elemTd in lstTdElem)
                        {
                            // "\t\t" is used for Tab Space between two Text
                            strRowData = strRowData + elemTd.Text + "\t\t";
                            var t = elemTd.GetAttribute("class");
                            if (t.Contains("game_date"))
                            {
                                temp.GameTime = Convert.ToDateTime(elemTd.Text);
                            }
                            else if (t.Contains("game_time"))
                            {

                            }
                            else if (t.Contains("matchup"))
                            {
                                temp.HomeTeam = elemTd.Text.Substring(elemTd.Text.IndexOf("@") + 2, 3);
                                temp.AwayTeam = elemTd.Text.Substring(0, 3);
                            }
                            else if (t.Contains("play"))
                            {
                                if (elemTd.Text.Contains(" +") && !elemTd.Text.Contains("(+") && !elemTd.Text.Contains("(-"))
                                {
                                    int dir = elemTd.Text.LastIndexOf(" +") + 2;
                                    int len = elemTd.Text.Length;
                                    temp.Play = elemTd.Text.Substring(0, dir - 2);
                                    temp.Line = Convert.ToInt32(elemTd.Text.Substring(dir, len - dir));
                                }
                                else if (elemTd.Text.Contains(" -") && !elemTd.Text.Contains("(+") && !elemTd.Text.Contains("(-"))
                                {
                                    int dir = elemTd.Text.LastIndexOf(" -") + 2;
                                    int len = elemTd.Text.Length;
                                    temp.Play = elemTd.Text.Substring(0, dir - 2);
                                    temp.Line = Convert.ToInt32(elemTd.Text.Substring(dir, len - dir)) * -1;
                                }
                                else if (elemTd.Text.Contains("(+"))
                                {
                                    int dir = elemTd.Text.LastIndexOf("(+") + 2;
                                    int len = elemTd.Text.Length - 1;
                                    temp.Play = elemTd.Text.Substring(0, dir - 3);
                                    temp.Line = Convert.ToInt32(elemTd.Text.Substring(dir, len - dir));
                                }
                                else if (elemTd.Text.Contains("(-"))
                                {
                                    int dir = elemTd.Text.LastIndexOf("(-") + 2;
                                    int len = elemTd.Text.Length - 1;
                                    temp.Play = elemTd.Text.Substring(0, dir - 3);
                                    temp.Line = Convert.ToInt32(elemTd.Text.Substring(dir, len - dir));
                                }
                                else
                                {
                                    temp.Play = elemTd.Text;
                                }
                                if (temp.Play.Contains("Over"))
                                {
                                    temp.Bet = PlayType.OverPointsLine;
                                }
                                else if (temp.Play.Contains("Under"))
                                {
                                    temp.Bet = PlayType.UnderPointsLine;
                                }
                                else if (temp.Play.Contains("+") || temp.Play.Contains("-"))
                                {
                                    if (temp.Play.Contains(temp.HomeTeam))
                                    {
                                        temp.Bet = PlayType.HomeHandicap;
                                    }
                                    else if (temp.Play.Contains(temp.AwayTeam))
                                    {
                                        temp.Bet = PlayType.AwayHandicap;
                                    }
                                }
                                else if (temp.Play.Contains(temp.HomeTeam))
                                {
                                    temp.Bet = PlayType.HomeMoneyLine;
                                }
                                else if (temp.Play.Contains(temp.AwayTeam))
                                {
                                    temp.Bet = PlayType.AwayMoneyLine;
                                }
                            }
                            else if (t.Contains("release"))
                            {
                                var n = DateTime.Now.Year.ToString() + "/" + elemTd.Text;
                                try { temp.ReleaseTime = DateTime.ParseExact(n, "yyyy/MM/dd hh:mmtt", null); }
                                catch
                                {
                                    try { temp.ReleaseTime = DateTime.ParseExact(n, "yyyy/MM/dd hh:mm tt", null); }
                                    catch { temp.ReleaseTime = temp.GameTime; }
                                }
                            }
                            else if (t.Contains("result"))
                            {
                                temp.Result = ResultConverter(elemTd.Text);
                            }
                        }
                        header = false;
                        bets.Add(temp);
                    }
                    else
                    {
                        // To print the data into the console
                        Console.WriteLine("This is Header Row");
                        Console.WriteLine(lstTrElem[0].Text.Replace(" ", "\t\t"));
                    }
                    Console.WriteLine(strRowData);
                    strRowData = String.Empty;
                }
                Console.WriteLine("");

                chrome.Quit();
                return bets;
            }
        }

        #endregion

        #region Pinnacle Methods
        public static async void PlaceBets(List<BettingLines> Bets)
        {
            using (var httpClient =
                    HttpClientFactory.GetNewInstance("chrisholley23@gmail.com", "Fl@mingo23"))
            {
                var api = new PinnacleClient("USD", OddsFormat.AMERICAN, httpClient);

                long lastFixture = 0;
                long lastLine = 0;

                var fixtures = await api.GetFixtures(new GetFixturesRequest(4, lastFixture));

                var lines = await api.GetOdds(new GetOddsRequest(fixtures.SportId,
                    fixtures.Leagues.Select(i => i.Id).ToList(), lastLine, false));

                var leagues = await api.GetLeagues(4);

                // Subsequent calls to GetOdds or GetFixtures should pass these 'Last' values to get only what changed since instead of the full snapshot
                lastFixture = fixtures.Last;
                lastLine = lines.Last;

            }
            //var httpClient = HttpClientFactory.GetNewInstance("CH1160434", "Fl@mingo23", true, "https://api.pinnacle.com/");

            //PinnacleClient _pinnacleClient = new PinnacleClient("USD", OddsFormat.AMERICAN, httpClient);


            //var fixtures = await _pinnacleClient.GetFixtures(new GetFixturesRequest(33));

            //List<Sport> _sports = await _pinnacleClient.GetSports();

            //GetFixturesRequest MLB = new GetFixturesRequest(0);

            //_pinnacleClient.GetFixtures()
            //var fixtures = await _pinnacleClient.GetFixtures(MLB);

        }

        #endregion

        #region Bookmaker Methods
        public static void PlaceBookmakerBets(List<BettingLines> Bets, List<BettingLines> placedBets = null)
        {
            using (var chrome = new ChromeDriver(Environment.CurrentDirectory))
            {
                var userName = "";
                var password = "";
                bool header = true;
                double maxSize = 175, positionSize = 20, maxNumbets = 75;
                int numBetsToPlace = Bets.Count();

                chrome.Url = "https://www.bookmaker.eu/loginpage";
                chrome.Navigate();
                try
                {
                    chrome.FindElementById("account").SendKeys(userName);
                    chrome.FindElementById("password").SendKeys(password);
                    chrome.FindElementByXPath("//*[@id='loginBox']/input[3]").Click();
                }
                catch
                {
                    Console.WriteLine("Unable to log in to Bookmaker, ending program now");
                    throw;
                }

                int count = 0;
                while (chrome.Url.Contains("https://be.bookmaker.eu/en/login?lk="))
                {
                    Thread.Sleep(500);
                    count++;
                    if (count > 8)
                        break;
                }

                if (placedBets == null || Bets.Where(x => x.ReleaseTime.Date < DateTime.Now.Date).Any())
                {
                    placedBets = GetOpenBets();
                }
                if (placedBets != null || placedBets.Count() > 0) { Bets = RemoveOpenBets(Bets, placedBets); }
                
                Dictionary<Leagues, List<BettingLines>> SortedBets = new Dictionary<Leagues, List<BettingLines>>();
                SortedBets.Add(Leagues.MLB, Bets.Where(a => a.League == Leagues.MLB).ToList());
                SortedBets.Add(Leagues.NCAAB, Bets.Where(a => a.League == Leagues.NCAAB).ToList());
                SortedBets.Add(Leagues.NCAAF, Bets.Where(a => a.League == Leagues.NCAAF).ToList());
                SortedBets.Add(Leagues.NFL, Bets.Where(a => a.League == Leagues.NFL).ToList());

                double accountSize = 0;

                foreach (KeyValuePair<Leagues, List<BettingLines>> legs in SortedBets)
                {
                    if (legs.Value.Count() < 1)
                        continue;
                    if (legs.Key == Leagues.MLB)
                    {
                        //continue;
                        List<BettingLines> mlbHalf = legs.Value.Where(a => a.Play.Contains("(F5)") || a.FirstHalf).ToList();
                        List<BettingLines> mlbFull = legs.Value.Where(a => !(a.Play.Contains("(F5)") || a.FirstHalf)).ToList();
                        if (mlbHalf.Count() > 0)
                        {
                            chrome.Url = "https://be.bookmaker.eu/en/sports/baseball/mlb-1st-5-full-innings/";
                            ComponentLoaderPause();
                            BetSlipCreator(mlbHalf, "MLB", numBetsToPlace);
                            SetPositionSizes(Bets.Count(), legs.Key);
                        }
                        if (mlbFull.Count() > 0)
                        {
                            chrome.Url = "https://be.bookmaker.eu/en/sports/baseball/major-league-baseball/";
                            ComponentLoaderPause();
                            BetSlipCreator(mlbFull, "MLB", numBetsToPlace);
                            SetPositionSizes(Bets.Count(), legs.Key);
                        }
                    }
                    else if (legs.Key == Leagues.NFL)
                    {
                        List<BettingLines> nflHalf = legs.Value.Where(a => a.Play.Contains("(1H)") || a.FirstHalf).ToList();
                        List<BettingLines> nflFull = legs.Value.Where(a => !(a.Play.Contains("(1H)") || a.FirstHalf)).ToList();
                        if (nflFull.Count() > 0)
                        {
                            chrome.Url = "https://be.bookmaker.eu/en/sports/football/nfl/";
                            ComponentLoaderPause();
                            BetSlipCreator(nflFull, "NFL", numBetsToPlace);
                            SetPositionSizes(Bets.Count(), legs.Key);
                        }
                        if (nflHalf.Count() > 0)
                        {
                            chrome.Url = "https://be.bookmaker.eu/en/sports/football/nfl-1st-halves/";
                            ComponentLoaderPause();
                            BetSlipCreator(nflHalf, "NFL", numBetsToPlace);
                            SetPositionSizes(Bets.Count(), legs.Key);
                        }
                    }
                    else if (legs.Key == Leagues.NCAAB && chrome.Url != "")
                    {
                        chrome.Url = "";
                        ComponentLoaderPause();
                        chrome.Navigate();
                    }
                    else if (legs.Key == Leagues.NCAAF)
                    {
                        //continue;
                        List<BettingLines> ncaafHalf = legs.Value.Where(a => a.Play.Contains("(1H)") || a.FirstHalf).ToList();
                        List<BettingLines> ncaafFull = legs.Value.Where(a => !(a.Play.Contains("(1H)") || a.FirstHalf)).ToList();
                        if (ncaafFull.Count() > 0)
                        {
                            
                            chrome.Url = "https://be.bookmaker.eu/en/sports/football/college-football/";
                            ComponentLoaderPause();
                            BetSlipCreator(ncaafFull, "CFB", numBetsToPlace);
                            SetPositionSizes(Bets.Count(), legs.Key);
                        }
                        if (ncaafHalf.Count() > 0)
                        {
                            chrome.Url = "https://be.bookmaker.eu/en/sports/football/ncaa-f-1st-halves/";
                            ComponentLoaderPause();
                            BetSlipCreator(ncaafHalf, "CFB", numBetsToPlace);
                            SetPositionSizes(Bets.Count(), legs.Key);
                        }
                    }
                    else
                        continue;
                }


                chrome.Quit();


                void ComponentLoaderPause()
                {
                    bool loading = true;
                    while (loading)
                    {
                        loading = chrome.FindElements(By.ClassName("component-loader")).Any();
                        Thread.Sleep(500);
                    }
                }

                void BetSlipCreator(List<BettingLines> legs, string sport, int numBetstoPlace)
                {
                    if (accountSize == 0)
                    {
                        accountSize = Convert.ToDouble(chrome.FindElementById("dropdownAmount").Text.Substring(1));
                        IWebElement accountDropdown = chrome.FindElementsByClassName("dropdown-menu").Where(a => a.GetAttribute("aria-labelledby") == "dropdownAmount").First();
                        chrome.FindElementById("dropdownAmount").Click();
                        string total = accountDropdown.FindElements(By.ClassName("dropdown-item"))[2].Text;
                        total = total.Substring(total.IndexOf("C")+1);
                        try { accountSize = Convert.ToDouble(total); }
                        catch { }
                        positionSize = numBetstoPlace > maxNumbets ? (accountSize / (numBetstoPlace) > maxSize ? maxSize : accountSize / (numBetstoPlace)) : (accountSize / (maxNumbets) > maxSize ? maxSize : accountSize / (maxNumbets));
                    }
                    List<IWebElement> brokenbox = new List<IWebElement>();
                    List<IWebElement> CFBchkBoxes = new List<IWebElement>();
                    try
                    {
                        IWebElement boxSelector = chrome.FindElementByClassName("sports-league");//*[@id="match_120_0"]
                        brokenbox = boxSelector.FindElements(By.ClassName("sports-league-game")).Where(a => a.GetAttribute("type") == "h_alt" && !a.GetAttribute("class").Contains("derivate")).ToList();
                    }
                    catch
                    {
                        Thread.Sleep(5000);
                        ComponentLoaderPause();
                        IWebElement boxSelector = chrome.FindElementByClassName("sports-league");//*[@id="match_120_0"]
                        brokenbox = boxSelector.FindElements(By.ClassName("sports-league-game")).Where(a => a.GetAttribute("type") == "h_alt" && !a.GetAttribute("class").Contains("derivate")).ToList();
                    }

                    foreach (var box in brokenbox)
                    {
                        CFBchkBoxes.AddRange(box.FindElements(By.ClassName("chkbox")).Where(a => a.GetAttribute("sport") == sport));
                    }
                    IWebElement chkBox;
                    foreach (BettingLines legbets in legs)
                    {
                        try
                        {
                            if (legbets.Bet == PlayType.OverPointsLine)
                            {
                                chkBox = CFBchkBoxes.Where(a => a.GetAttribute("position") == "over" && a.GetAttribute("team").Contains(legbets.AwayTeam)).First().FindElement(By.XPath(".."));
                                chkBox.Click();
                            }
                            else if (legbets.Bet == PlayType.UnderPointsLine)
                            {
                                chkBox = CFBchkBoxes.Where(a => a.GetAttribute("position") == "under" && a.GetAttribute("team").Contains(legbets.AwayTeam)).First().FindElement(By.XPath(".."));
                                chkBox.Click();
                            }
                            else if (legbets.Bet == PlayType.HomeHandicap)
                            {
                                chkBox = CFBchkBoxes.Where(a => a.GetAttribute("linetype") == "spread" && a.GetAttribute("team").Contains(legbets.HomeTeam)).First().FindElement(By.XPath(".."));
                                chkBox.Click();
                            }
                            else if (legbets.Bet == PlayType.HomeMoneyLine)
                            {
                                chkBox = CFBchkBoxes.Where(a => a.GetAttribute("linetype") == "odds" && a.GetAttribute("team").Contains(legbets.HomeTeam)).First().FindElement(By.XPath(".."));
                                chkBox.Click();
                            }
                            else if (legbets.Bet == PlayType.AwayHandicap)
                            {
                                chkBox = CFBchkBoxes.Where(a => a.GetAttribute("linetype") == "spread" && a.GetAttribute("team").Contains(legbets.AwayTeam)).First().FindElement(By.XPath(".."));
                                chkBox.Click();
                            }
                            else if (legbets.Bet == PlayType.AwayMoneyLine)
                            {
                                chkBox = CFBchkBoxes.Where(a => a.GetAttribute("linetype") == "odds" && a.GetAttribute("team").Contains(legbets.AwayTeam)).First().FindElement(By.XPath(".."));
                                chkBox.Click();
                            }
                            else
                            {
                                Console.WriteLine("Unable to place bet for play : " + legbets.Play);
                            }
                        }
                        catch
                        {
                            Console.WriteLine("Unable to place bet for play : " + legbets.Play);
                        }
                        chrome.Navigate();
                    }

                }

                void SetPositionSizes(int numBets, Leagues _league)
                {
                    IWebElement betslip = chrome.FindElementByClassName("betslip-selections");
                    List<IWebElement> bets = new List<IWebElement>(betslip.FindElements(By.ClassName("bet-content")));
                    List<IWebElement> wins = new List<IWebElement>(betslip.FindElements(By.XPath("//*[contains(@placeholder,'Win')]")));
                    List<IWebElement> risks = new List<IWebElement>(betslip.FindElements(By.XPath("//*[contains(@placeholder,'Risk')]")));
                    
                    positionSize = Math.Round(positionSize, 0);
                    //positionSize = 70;
                    List<IWebElement> CFBchkBoxes = new List<IWebElement>();
                    int i = 0;
                    foreach (var bet in bets)
                    {
                        int odds = -1;
                        try
                        {
                            if (_league == Leagues.MLB)
                            {
                                odds = Convert.ToInt32(bet.FindElement(By.ClassName("odds")).Text);
                                if (bet.FindElements(By.Id("dropdownheader")).Any())
                                {
                                    IWebElement action = bet.FindElements(By.Id("dropdownheader")).First();
                                    action.Click();
                                    if (bet.FindElements(By.XPath("//*[contains(@id,'Pitchers')]")).Where(a => !a.Text.Contains("Action")).Any())
                                    {
                                        bet.FindElements(By.XPath("//*[contains(@id,'Pitchers')]")).Where(a => !a.Text.Contains("Action")).First().Click();
                                    }
                                    else
                                    {
                                        action.FindElements(By.Id("dropdownheader")).First().Click();
                                    }
                                }
                            }
                            else if (_league == Leagues.NCAAF)
                            {
                                if (bet.FindElements(By.Id("dropdownheader")).Any())
                                {
                                    string betPlay = bet.FindElements(By.Id("dropdownheader")).First().Text;
                                    int plus = betPlay.LastIndexOf("+");
                                    int minus = betPlay.LastIndexOf("-");
                                    if (minus > plus)
                                    {
                                        odds = Convert.ToInt32(betPlay.Substring(minus, betPlay.Length - minus));
                                    }
                                    else if (plus > minus)
                                    {
                                        odds = Convert.ToInt32(betPlay.Substring(plus, betPlay.Length - plus - 1));
                                    }
                                }

                            }
                            else if (_league == Leagues.NFL)
                            {

                            }
                        }
                        
                        catch(Exception ex)
                        {
                            Console.WriteLine("Error detecting odds for bet number " + i + " - Exception: " + ex.Message);
                        }
                        try
                        {
                            if (odds < 0)
                            {
                                wins[i].Click();
                                wins[i].SendKeys(positionSize.ToString());
                            }
                            else
                            {
                                risks[i].Click();
                                risks[i].SendKeys(positionSize.ToString());
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error setting position size for bet number " + i + " - Exception: " + ex.Message);
                        }
                        i++;
                       // CFBchkBoxes.AddRange(box.FindElements(By.ClassName("chkbox")).Where(a => a.GetAttribute("sport") == sport));
                    }
                    
                        
                }

                List<BettingLines> GetOpenBets()
                {
                    List<BettingLines> temp = new List<BettingLines>();
                    chrome.Url = "https://be.bookmaker.eu/en/sports/playeropenbets.aspx";
                    try
                    {
                        List<IWebElement> openBetInfo = chrome.FindElements(By.ClassName("list-details-ticket")).ToList();
                        int len = 0;
                        string enter = "\r\n";
                        foreach (var elem in openBetInfo)
                        {
                            string placedDate = "", risk, win, line = "", gameTime = "", ticketInfo, leag = "";
                            try
                            {
                                placedDate = elem.FindElement(By.XPath("..")).FindElement(By.XPath("..")).FindElement(By.ClassName("date-time")).Text;
                                placedDate = placedDate.Replace("\r\n", " ");
                                risk = elem.FindElement(By.ClassName("rw-ticket")).FindElements(By.XPath(".//*")).First().Text;
                                win = risk.Substring(risk.IndexOf("Win: ") + 5);
                                risk = risk.Substring(risk.IndexOf(" ") + 1, risk.IndexOf(" -") - risk.IndexOf(" "));
                                line = elem.FindElement(By.ClassName("pick-ticket")).Text;
                                line = line.Replace("\r\n", "----");
                                gameTime = line.Substring(line.LastIndexOf("Game start ") + 11);

                                ticketInfo = elem.FindElement(By.ClassName("info-ticket")).FindElements(By.XPath(".//*")).First().Text;
                                leag = line.Substring(0, line.IndexOf(" "));
                            }
                            catch(Exception ex)
                            {
                                Console.WriteLine("Error finding open bet information : " + ex.Message);
                            }

                            BettingLines or = new BettingLines();
                            or.League = LeagueConverter(leag);
                            try
                            {
                                or.GameTime = DateTime.ParseExact(gameTime, "MM/dd/yyyy  hh:mm tt", null);
                                or.ReleaseTime = DateTime.ParseExact(placedDate, "MM/dd/yyyy hh:mm tt", null);
                            }
                            catch { Console.WriteLine("Error setting Open Bet times"); }

                            try
                            {
                                if (line.Contains("TOTAL"))
                                {
                                    int beg = line.IndexOf("(") + 1;
                                    int vrs = line.IndexOf(" vrs ");
                                    or.AwayTeam = line.Substring(beg, vrs - beg);
                                    if (or.AwayTeam.Contains("1H"))
                                    {
                                        or.FirstHalf = true;
                                        or.AwayTeam = or.AwayTeam.Substring(or.AwayTeam.LastIndexOf("1H") + 3);
                                    }
                                    string home = line.Substring(vrs + 5);
                                    or.HomeTeam = home.Substring(0, home.IndexOf(")"));

                                    if (or.HomeTeam.Contains("1H"))
                                    {
                                        or.FirstHalf = true;
                                        or.HomeTeam = or.HomeTeam.Substring(or.HomeTeam.LastIndexOf("1H") + 3);
                                    }

                                    len = line.Length - 1;
                                    int tot = line.IndexOf("TOTAL") + 6;
                                    string play = line.Substring(tot, beg - tot - 5);
                                    if (play.Contains("u"))
                                    {
                                        play = play.Replace("u", "Under ");
                                        or.Play = play;
                                        or.Bet = PlayType.UnderPointsLine;
                                    }
                                    else if (play.Contains("o"))
                                    {
                                        play = play.Replace("o", "Over ");
                                        or.Play = play;
                                        or.Bet = PlayType.OverPointsLine;
                                    }

                                    if (play.Contains(" +") && !play.Contains("(+") && !play.Contains("(-"))
                                    {
                                        int dir = play.LastIndexOf(" +") + 2;
                                        len = play.Length;
                                        or.Play = play.Substring(0, dir - 1);
                                        or.Line = Convert.ToInt32(play.Substring(dir, len - dir));
                                    }
                                    else if (play.Contains(" -") && !play.Contains("(+") && !play.Contains("(-"))
                                    {
                                        int dir = play.LastIndexOf(" -") + 2;
                                        len = play.Length;
                                        or.Play = play.Substring(0, dir - 1);
                                        or.Line = Convert.ToInt32(play.Substring(dir, len - dir)) * -1;
                                    }
                                    else if (play.Contains("(+"))
                                    {
                                        int dir = play.LastIndexOf("(+") + 2;
                                        len = play.Length - 1;
                                        or.Play = play.Substring(0, dir - 3);
                                        or.Line = Convert.ToInt32(play.Substring(dir, len - dir));
                                    }
                                    else if (play.Contains("(-"))
                                    {
                                        int dir = play.LastIndexOf("(-") + 2;
                                        len = play.Length - 1;
                                        or.Play = play.Substring(0, dir - 3);
                                        or.Line = Convert.ToInt32(play.Substring(dir, len - dir));
                                    }
                                    else if (play.Contains("-"))
                                    {
                                        int dir = play.LastIndexOf("-") + 1;
                                        len = play.Length;
                                        or.Play = play.Substring(0, dir - 1);
                                        or.Line = Convert.ToInt32(play.Substring(dir, len - dir)) * -1;
                                    }
                                    else if (play.Contains("+"))
                                    {
                                        int dir = play.LastIndexOf("+") + 1;
                                        len = play.Length;
                                        or.Play = play.Substring(0, dir - 1);
                                        or.Line = Convert.ToInt32(play.Substring(dir, len - dir)) * -1;
                                    }
                                    else
                                    {
                                        or.Play = play;
                                    }
                                }
                                else
                                {
                                    int beg = line.IndexOf("] ") + 2;
                                    int split = line.Substring(beg).IndexOf("----") + beg;
                                    string play = line.Substring(beg, split - beg);
                                    if (play.Substring(0, 3).Contains("1H"))
                                    {
                                        or.FirstHalf = true;
                                        play = play.Substring(play.LastIndexOf("1H") + 3);
                                    }
                                    if (play.Contains(" +") && play.Count(a => a == '+') < 2 && !play.Contains("-"))
                                    {
                                        int dir = play.LastIndexOf(" +") + 2;
                                        len = play.Length;
                                        or.Play = play.Substring(0, dir - 2);
                                        or.Line = Convert.ToInt32(play.Substring(dir, len - dir));
                                        or.Bet = PlayType.MoneyLine;
                                        or.HomeTeam = play.Substring(0, dir - 2);
                                        or.AwayTeam = play.Substring(0, dir - 2);
                                    }
                                    else if (play.Contains(" -") && play.Count(a => a == '-') < 2 && !play.Contains("+"))
                                    {
                                        int dir = play.LastIndexOf(" -") + 2;
                                        len = play.Length;
                                        or.Play = play.Substring(0, dir - 1);
                                        or.Line = Convert.ToInt32(play.Substring(dir, len - dir)) * -1;
                                        or.Bet = PlayType.MoneyLine;
                                        or.HomeTeam = play.Substring(0, dir - 2);
                                        or.AwayTeam = play.Substring(0, dir - 2);
                                    }
                                    else if (play.Count(a => a == '-') > 1)
                                    {
                                        int dir = play.LastIndexOf("-") + 1;
                                        len = play.Length;
                                        or.Play = play.Substring(0, dir - 2);
                                        or.Line = Convert.ToInt32(play.Substring(dir, len - dir)) * -1;
                                        or.Bet = PlayType.Handicap;
                                        or.HomeTeam = play.Substring(0, play.IndexOf(" -"));
                                        or.AwayTeam = play.Substring(0, play.IndexOf(" -"));
                                    }
                                    else if (play.Count(a => a == '+') > 1)
                                    {
                                        int dir = play.LastIndexOf("+") + 2;
                                        len = play.Length;
                                        or.Play = play.Substring(0, dir - 2);
                                        or.Line = Convert.ToInt32(play.Substring(dir, len - dir));
                                        or.Bet = PlayType.Handicap;
                                        or.HomeTeam = play.Substring(0, play.IndexOf(" +"));
                                        or.AwayTeam = play.Substring(0, play.IndexOf(" +"));
                                    }
                                    else if (play.Contains("+") && play.Contains("-"))
                                    {
                                        int plus = play.IndexOf("+");
                                        int minus = play.IndexOf("-");
                                        len = play.Length;
                                        if (plus > minus)
                                        {
                                            or.Play = play.Substring(0, plus);
                                            or.Line = Convert.ToInt32(play.Substring(plus, len - plus));
                                            or.HomeTeam = play.Substring(0, minus - 2);
                                            or.AwayTeam = play.Substring(0, minus - 2);
                                        }
                                        else if (minus > plus)
                                        {
                                            or.Play = play.Substring(0, minus);
                                            or.Line = Convert.ToInt32(play.Substring(minus, len - minus));
                                            or.HomeTeam = play.Substring(0, plus - 2);
                                            or.AwayTeam = play.Substring(0, plus - 2);
                                        }
                                        or.Bet = PlayType.Handicap;
                                    }
                                    else
                                    {
                                        or.Play = play;
                                    }

                                }

                            }
                            catch
                            {
                                Console.WriteLine("Error Recording play");
                            }
                            temp.Add(or);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }

                    return temp;
                }

                List<BettingLines> RemoveOpenBets(List<BettingLines> BetsToPlace, List<BettingLines> PlacedBets)
                {
                    BetsToPlace = BetsToPlace.Where(x => x.ReleaseTime.Date >= DateTime.Now.Date).ToList();
                    List<BettingLines> final = BetsToPlace;
                    List<BettingLines> filter = PlacedBets;
                    
                    if (PlacedBets.Any())
                    {
                        for (int i = 0; i < BetsToPlace.Count(); i++)
                        {
                            try
                            {
                                if (i + 1 > BetsToPlace.Count())
                                    break;
                                if (PlacedBets.Where(a => a.League == BetsToPlace[i].League).Any())
                                {
                                    filter = PlacedBets.Where(a => a.League == BetsToPlace[i].League).ToList();
                                    //BetsToPlace.RemoveAt(i);
                                    if (filter.Where(a => a.HomeTeam == BetsToPlace[i].HomeTeam || a.AwayTeam == BetsToPlace[i].AwayTeam).Any())
                                    {
                                        filter = filter.Where(a => a.HomeTeam == BetsToPlace[i].HomeTeam || a.AwayTeam == BetsToPlace[i].AwayTeam).ToList();
                                        if (filter.Where(a => a.FirstHalf == BetsToPlace[i].FirstHalf).Any())
                                        {
                                            filter = filter.Where(a => a.FirstHalf == BetsToPlace[i].FirstHalf).ToList();
                                            if (BetsToPlace[i].Play.Contains("Over") && filter.Where(a => a.Play.Contains("Over")).Count() == 1)
                                            {
                                                BetsToPlace.RemoveAt(i);
                                                i--;
                                            }
                                            else if (BetsToPlace[i].Play.Contains("Under") && filter.Where(a => a.Play.Contains("Under")).Count() == 1)
                                            {
                                                BetsToPlace.RemoveAt(i);
                                                i--;
                                            }
                                            else if (BetsToPlace[i].Play.Contains("+") && filter.Where(a => a.Play.Contains("+")).Count() == 1)
                                            {
                                                var p = filter.Where(a => a.Play.Contains("+")).First().Play;
                                                p = p.Substring(0, p.IndexOf("+"));
                                                if (BetsToPlace[i].Play.Substring(0, BetsToPlace[i].Play.IndexOf("+")).Contains(p))
                                                {
                                                    BetsToPlace.RemoveAt(i);
                                                    i--;
                                                }
                                            }
                                            else if (BetsToPlace[i].Play.Contains("-") && filter.Where(a => a.Play.Contains("-")).Count() == 1)
                                            {
                                                var p = filter.Where(a => a.Play.Contains("-")).First().Play;
                                                p = p.Substring(0, p.IndexOf("-"));
                                                if (BetsToPlace[i].Play.Substring(0, BetsToPlace[i].Play.IndexOf("-")).Contains(p))
                                                {
                                                    BetsToPlace.RemoveAt(i);
                                                    i--;
                                                }
                                            }
                                            else if (filter.Where(a => a.Play.Contains(BetsToPlace[i].Play)).Any())
                                            {
                                                BetsToPlace.RemoveAt(i);
                                                i--;
                                            }
                                            else
                                            {
                                                var q = BetsToPlace[i].Play;
                                                var te = filter.First().Play;
                                            }
                                        }
                                    }

                                    if (i + 2 > BetsToPlace.Count() || BetsToPlace.Count() < 1)
                                        break;
                                }
                                else
                                    continue;
                            }
                            catch
                            {
                                Console.WriteLine("Error removing place bet at " + i);
                            }
                        }
                    }
                    final = BetsToPlace;
                    return final;
                }
            }



        }

        public static List<BettingLines> GetOpenBets()
        {
            using (var chrome = new ChromeDriver(Environment.CurrentDirectory))
            {
                var userName = "";
                var password = "";

                chrome.Url = "https://www.bookmaker.eu/loginpage";
                chrome.Navigate();
                try
                {
                    chrome.FindElementById("account").SendKeys(userName);
                    chrome.FindElementById("password").SendKeys(password);
                    chrome.FindElementByXPath("//*[@id='loginBox']/input[3]").Click();
                }
                catch
                {
                    Console.WriteLine("Unable to log in to Bookmaker, skipping open bet check");
                }

                ComponentLoaderPause(chrome);
                List<BettingLines> temp = new List<BettingLines>();
                chrome.Url = "https://be.bookmaker.eu/en/sports/playeropenbets.aspx";
                try
                {
                    List<IWebElement> openBetInfo = chrome.FindElements(By.ClassName("list-details-ticket")).ToList();
                    int len = 0;
                    foreach (var elem in openBetInfo)
                    {
                        string placedDate = "", risk, win, line = "", gameTime = "", ticketInfo, leag = "";
                        try
                        {
                            placedDate = elem.FindElement(By.XPath("..")).FindElement(By.XPath("..")).FindElement(By.ClassName("date-time")).Text;
                            placedDate = placedDate.Replace("\r\n", " ");
                            risk = elem.FindElement(By.ClassName("rw-ticket")).FindElements(By.XPath(".//*")).First().Text;
                            win = risk.Substring(risk.IndexOf("Win: ") + 5);
                            risk = risk.Substring(risk.IndexOf(" ") + 1, risk.IndexOf(" -") - risk.IndexOf(" "));
                            line = elem.FindElement(By.ClassName("pick-ticket")).Text;
                            gameTime = line.Substring(line.LastIndexOf("Game start ") + 11);

                            ticketInfo = elem.FindElement(By.ClassName("info-ticket")).FindElements(By.XPath(".//*")).First().Text;
                            leag = line.Substring(0, line.IndexOf(" "));
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error finding open bet information : " + ex.Message);
                        }

                        BettingLines or = new BettingLines();
                        or.League = LeagueConverter(leag);
                        try
                        {
                            or.GameTime = DateTime.ParseExact(gameTime, "MM/dd/yyyy  hh:mm tt", null);
                            or.ReleaseTime = DateTime.ParseExact(placedDate, "MM/dd/yyyy hh:mm tt", null);
                        }
                        catch { Console.WriteLine("Error setting Open Bet times"); }
                        if (line.Contains("TOTAL"))
                        {
                            int beg = line.IndexOf("(") + 1;
                            int vrs = line.IndexOf(" vrs ");
                            or.AwayTeam = line.Substring(beg, vrs - beg);
                            if (or.AwayTeam.Contains("1H"))
                            {
                                or.FirstHalf = true;
                                or.AwayTeam = or.AwayTeam.Substring(or.AwayTeam.LastIndexOf("1H") + 3);
                            }
                            string home = line.Substring(vrs + 5);
                            or.HomeTeam = home.Substring(0, home.IndexOf(")"));

                            if (or.HomeTeam.Contains("1H"))
                            {
                                or.FirstHalf = true;
                                or.HomeTeam = or.HomeTeam.Substring(or.HomeTeam.LastIndexOf("1H") + 3);
                            }

                            len = line.Length - 1;
                            int tot = line.IndexOf("TOTAL") + 6;
                            string play = line.Substring(tot, beg - tot - 2);
                            if (play.Contains("u"))
                            {
                                or.Play = play;
                                or.Bet = PlayType.UnderPointsLine;
                            }
                            else if (play.Contains("o"))
                            {
                                or.Play = play;
                                or.Bet = PlayType.OverPointsLine;
                            }

                            if (play.Contains(" +") && !play.Contains("(+") && !play.Contains("(-"))
                            {
                                int dir = play.LastIndexOf(" +") + 2;
                                len = play.Length;
                                or.Play = play.Substring(0, dir - 1);
                                or.Line = Convert.ToInt32(play.Substring(dir, len - dir));
                            }
                            else if (play.Contains(" -") && !play.Contains("(+") && !play.Contains("(-"))
                            {
                                int dir = play.LastIndexOf(" -") + 2;
                                len = play.Length;
                                or.Play = play.Substring(0, dir - 1);
                                or.Line = Convert.ToInt32(play.Substring(dir, len - dir)) * -1;
                            }
                            else if (play.Contains("(+"))
                            {
                                int dir = play.LastIndexOf("(+") + 2;
                                len = play.Length - 1;
                                or.Play = play.Substring(0, dir - 3);
                                or.Line = Convert.ToInt32(play.Substring(dir, len - dir));
                            }
                            else if (play.Contains("(-"))
                            {
                                int dir = play.LastIndexOf("(-") + 2;
                                len = play.Length - 1;
                                or.Play = play.Substring(0, dir - 3);
                                or.Line = Convert.ToInt32(play.Substring(dir, len - dir));
                            }
                            else if (play.Contains("-"))
                            {
                                int dir = play.LastIndexOf("-") + 1;
                                len = play.Length;
                                or.Play = play.Substring(0, dir - 1);
                                or.Line = Convert.ToInt32(play.Substring(dir, len - dir)) * -1;
                            }
                            else if (play.Contains("+"))
                            {
                                int dir = play.LastIndexOf("+") + 1;
                                len = play.Length;
                                or.Play = play.Substring(0, dir - 1);
                                or.Line = Convert.ToInt32(play.Substring(dir, len - dir)) * -1;
                            }
                            else
                            {
                                or.Play = play;
                            }
                        }
                        else
                        {
                            int beg = line.IndexOf("] ") + 2;
                            string play = line.Substring(beg, line.IndexOf("\r") - beg);
                            if (play.Substring(0, 3).Contains("1H"))
                            {
                                or.FirstHalf = true;
                                play = play.Substring(play.LastIndexOf("1H") + 3);
                            }
                            if (play.Contains(" +") && play.Count(a => a == '+') < 2 && !play.Contains("-"))
                            {
                                int dir = play.LastIndexOf(" +") + 2;
                                len = play.Length;
                                or.Play = play.Substring(0, dir - 2);
                                or.Line = Convert.ToInt32(play.Substring(dir, len - dir));
                                or.Bet = PlayType.MoneyLine;
                                or.HomeTeam = play.Substring(0, dir - 2);
                                or.AwayTeam = play.Substring(0, dir - 2);
                            }
                            else if (play.Contains(" -") && play.Count(a => a == '-') < 2 && !play.Contains("+"))
                            {
                                int dir = play.LastIndexOf(" -") + 2;
                                len = play.Length;
                                or.Play = play.Substring(0, dir - 1);
                                or.Line = Convert.ToInt32(play.Substring(dir, len - dir)) * -1;
                                or.Bet = PlayType.MoneyLine;
                                or.HomeTeam = play.Substring(0, dir - 2);
                                or.AwayTeam = play.Substring(0, dir - 2);
                            }
                            else if (play.Count(a => a == '-') > 1)
                            {
                                int dir = play.LastIndexOf("-") + 1;
                                len = play.Length;
                                or.Play = play.Substring(0, dir - 2);
                                or.Line = Convert.ToInt32(play.Substring(dir, len - dir)) * -1;
                                or.Bet = PlayType.Handicap;
                                or.HomeTeam = play.Substring(0, play.IndexOf(" -"));
                                or.AwayTeam = play.Substring(0, play.IndexOf(" -"));
                            }
                            else if (play.Count(a => a == '+') > 1)
                            {
                                int dir = play.LastIndexOf("+") + 2;
                                len = play.Length;
                                or.Play = play.Substring(0, dir - 2);
                                or.Line = Convert.ToInt32(play.Substring(dir, len - dir));
                                or.Bet = PlayType.Handicap;
                                or.HomeTeam = play.Substring(0, play.IndexOf(" +"));
                                or.AwayTeam = play.Substring(0, play.IndexOf(" +"));
                            }
                            else if (play.Contains("+") && play.Contains("-"))
                            {
                                int plus = play.IndexOf("+");
                                int minus = play.IndexOf("-");
                                len = play.Length;
                                if (plus > minus)
                                {
                                    or.Play = play.Substring(0, plus);
                                    or.Line = Convert.ToInt32(play.Substring(plus, len - plus));
                                    or.HomeTeam = play.Substring(0, minus - 2);
                                    or.AwayTeam = play.Substring(0, minus - 2);
                                }
                                else if (minus > plus)
                                {
                                    or.Play = play.Substring(0, minus);
                                    or.Line = Convert.ToInt32(play.Substring(minus, len - minus));
                                    or.HomeTeam = play.Substring(0, plus - 2);
                                    or.AwayTeam = play.Substring(0, plus - 2);
                                }
                                or.Bet = PlayType.Handicap;
                            }
                            else
                            {
                                or.Play = play;
                            }

                        }
                        temp.Add(or);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                return temp;
            }
        }
        public static List<BettingLines> RemoveOpenBets(List<BettingLines> BetsToPlace, List<BettingLines> PlacedBets)
        {
            List<BettingLines> final = BetsToPlace;


            return final;
        }
        public static string MLBCreateURL(BettingLines bet, bool FirstHalf = false)
        {
            string hometeam = MLBConverter(bet.HomeTeam);
            string awayteam = MLBConverter(bet.AwayTeam);
            string url = "";
            if (FirstHalf)
            {
                url = "https://be.bookmaker.eu/en/sports/baseball/mlb-1st-5-full-innings/";
            }
            else
            {
                url = "https://be.bookmaker.eu/en/sports/baseball/major-league-baseball/";
            }
            
                url += awayteam + "-vs-" + hometeam + "/";
            return url;
        }
        public static void ComponentLoaderPause(ChromeDriver chrome)
        {
            bool loading = true;
            while (loading)
            {
                loading = chrome.FindElements(By.ClassName("component-loader")).Any();
                Thread.Sleep(500);
            }
        }


        #endregion

        #region GMail Methods
        public static string username = "";
        public static string pass = "";
        static void InitializeClient()
        {
            // Dispose of existing instance, if any.
            if (client != null)
                client.Dispose();
            client = new ImapClient("imap.gmail.com", 993, username, pass, AuthMethod.Login, true);
            // Setup event handlers.
            sendclient = new SmtpClient("smtp.gmail.com", 587);
            sendclient.EnableSsl = true;
            sendclient.UseDefaultCredentials = false;
            sendclient.Credentials = new NetworkCredential(username, pass);
            client.NewMessage += client_NewMessage;
            client.IdleError += client_IdleError;
        }

        static void client_IdleError(object sender, IdleErrorEventArgs e)
        {
            Console.WriteLine("An error occurred while idling: ");
            Console.Write(e.Exception.Message);
            reconnectEvent.Set();
        }

        static void client_NewMessage(object sender, IdleMessageEventArgs e)
        {
            MailMessage msg = client.GetMessage(e.MessageUID);
            Console.WriteLine("Got a new message, = " + msg.Subject);
            if ((msg.Subject.Contains("New Recommendations") || msg.Subject.Contains("Recommendations Are In")) && msg.Body.Contains("JAMBOS"))
            {
                Console.WriteLine(DateTime.Now.ToString() + " : New Recommendations Released, Retrieving Recommendations");
                _todaysLines = GetTodaysLines();
                Console.WriteLine("Recommendations Retreived, Sending via Text");
                SendTodaysLinesViaText(lineString);
                Console.WriteLine("Recommendations sent, placing daily bets");
                PlaceBookmakerBets(_todaysLines, _openBets);
                //_allLines = GetAllLines();
            }
        }
        
        static void SendTodaysLinesViaText(StringBuilder body = null)
        {
            if (sendclient == null)
            {
                sendclient = new SmtpClient("smtp.gmail.com", 587);
                sendclient.EnableSsl = true;
                sendclient.UseDefaultCredentials = false;
                sendclient.Credentials = new NetworkCredential(username, pass);
            }
            MailMessage msg = new MailMessage(new MailAddress(username + "@gmail.com"), new MailAddress(""));

            msg.Subject = string.Format("JAMBOS Lines are out");

            msg.Body = body.ToString();
            bool retry = true;
            try
            {
                sendclient.Send(msg);
                retry = true;
            }
            catch (Exception ex)
            {
                if (!retry)
                {
                    Console.WriteLine("Failed to send email reply to " + msg.To.ToString() + '.');
                    Console.WriteLine("Exception: " + ex.Message);
                    return;
                }

                retry = false;
            }
            finally
            {
                msg.Dispose();
            }
            

            Console.WriteLine("Email to SMS successfully sent.");
        }
        private static MailMessage CreateMessage()
        {
            MailMessage reply = new MailMessage(new MailAddress(username), new MailAddress(""));

            reply.Subject = string.Format("JAMBOS Lines are out");

            // Add body 
            StringBuilder body = new StringBuilder();

            reply.Body = body.ToString();

            return reply;
        }
        #endregion

        #region Classes
        public class BettingLines
        {
            public Leagues League { get; set; } = Leagues.UNK;
            public DateTime GameTime { get; set; }
            public DateTime ReleaseTime { get; set; }
            public string AwayTeam { get; set; }
            public string HomeTeam { get; set; }
            //Set this as an enum for different play types
            public string Play { get; set; }
            public PlayType Bet { get; set; }
            public bool FirstHalf { get; set; } = false;
            public int Line { get; set; }
            public Result Result { get; set; } = Result.Active;
            public int? Units { get; set; }
        }

        #endregion

        #region Enums
        public enum PlayType
        {
            HomeMoneyLine,
            AwayMoneyLine,
            MoneyLine,
            OverPointsLine,
            UnderPointsLine,
            HomeHandicap,
            AwayHandicap,
            Handicap
        }

        public enum Leagues
        {
            MLB, NFL, NCAAF, NCAAB, UNK
        }

        public enum MLB_Teams
        {
            NYY, TOR, LAA, BOS, PHI, SF, ATL, MIA, HOU, BAL, CLE, MIN, CHC, CIN, OAK, CWS, PIT, STL, KC, DET, TB, SEA, COL, SD, ARI, LAD, NYM, WSH, MIL, TEX
        }

        public enum NFL_Teams
        {
            Pittsburgh, Tennessee, SanFrancisco, KansasCity, Cleveland, TampaBay
        }

        public enum NCAAF_Teams
        {

        }
        public enum NCAAB_Teams
        {

        }
        
        public enum Result
        {
            Active, Win, Lose, Push
        }
#endregion

        #region Converters
        public static Leagues LeagueConverter(string League)
        {
            if (League == "MLB") { return Leagues.MLB; }
            else if (League == "NFL") { return Leagues.NFL; }
            else if (League == "CFB" || League == "NCAAF") { return Leagues.NCAAF; }
            else if (League == "NCAAB") { return Leagues.NCAAB; }
            else { return Leagues.UNK; }
        }

        public static Result ResultConverter(string res)
        {
            if (res == "W") { return Result.Win; }
            else if (res == "L") { return Result.Lose; }
            else if (res == "P") { return Result.Push; }
            else { return Result.Active; }
        }

        public static string MLBConverter(string Team, bool TitleCase = false)
        {
            string fullteam = "";
            if (TitleCase)
            {
                switch (Team)
                {
                    case "ATL":
                        fullteam = "atlanta braves";
                        break;
                    case "NYY":
                        fullteam = "new york yankees";
                        break;
                    case "TOR":
                        fullteam = "toronto blue jays";
                        break;
                    case "LAA":
                        fullteam = "los angeles angels";
                        break;
                    case "BOS":
                        fullteam = "boston red sox";
                        break;
                    case "PHI":
                        fullteam = "philadelphia phillies";
                        break;
                    case "SF":
                        fullteam = "san francisco giants";
                        break;
                    case "MIA":
                        fullteam = "miami marlins";
                        break;
                    case "HOU":
                        fullteam = "houston astros";
                        break;
                    case "BAL":
                        fullteam = "baltimore orioles";
                        break;
                    case "CLE":
                        fullteam = "cleveland indians";
                        break;
                    case "MIN":
                        fullteam = "minnesota twins";
                        break;
                    case "CHC":
                        fullteam = "chicago cubs";
                        break;
                    case "CIN":
                        fullteam = "cincinnati reds";
                        break;
                    case "OAK":
                        fullteam = "oakland athletics";
                        break;
                    case "CWS":
                        fullteam = "chicago white sox";
                        break;
                    case "PIT":
                        fullteam = "pittsburgh pirates";
                        break;
                    case "STL":
                        fullteam = "st. louis cardinals";
                        break;
                    case "KC":
                        fullteam = "kansas city royals";
                        break;
                    case "DET":
                        fullteam = "detroit tigers";
                        break;
                    case "TB":
                        fullteam = "tampa bay rays";
                        break;
                    case "SEA":
                        fullteam = "seattle mariners";
                        break;
                    case "COL":
                        fullteam = "colorado rockies";
                        break;
                    case "SD":
                        fullteam = "san diego padres";
                        break;
                    case "ARI":
                        fullteam = "";
                        break;
                    case "LAD":
                        fullteam = "los angeles dodgers";
                        break;
                    case "NYM":
                        fullteam = "new york mets";
                        break;
                    case "WSH":
                        fullteam = "washington nationals";
                        break;
                    case "MIL":
                        fullteam = "milwaukee brewers";
                        break;
                    case "TEX":
                        fullteam = "texas rangers";
                        break;


                }
                fullteam = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(fullteam);
            }
            else
            {
                switch (Team)
                {
                    case "ATL":
                        fullteam = "atlanta-braves";
                        break;
                    case "NYY":
                        fullteam = "new-york-yankees";
                        break;
                    case "TOR":
                        fullteam = "toronto-blue-jays";
                        break;
                    case "LAA":
                        fullteam = "los-angeles-angels";
                        break;
                    case "BOS":
                        fullteam = "boston-red-sox";
                        break;
                    case "PHI":
                        fullteam = "philadelphia-phillies";
                        break;
                    case "SF":
                        fullteam = "san-francisco-giants";
                        break;
                    case "MIA":
                        fullteam = "miami-marlins";
                        break;
                    case "HOU":
                        fullteam = "houston-astros";
                        break;
                    case "BAL":
                        fullteam = "baltimore-orioles";
                        break;
                    case "CLE":
                        fullteam = "cleveland-indians";
                        break;
                    case "MIN":
                        fullteam = "minnesota-twins";
                        break;
                    case "CHC":
                        fullteam = "chicago-cubs";
                        break;
                    case "CIN":
                        fullteam = "cincinnati-reds";
                        break;
                    case "OAK":
                        fullteam = "oakland-athletics";
                        break;
                    case "CWS":
                        fullteam = "chicago-white-sox";
                        break;
                    case "PIT":
                        fullteam = "pittsburgh-pirates";
                        break;
                    case "STL":
                        fullteam = "st-louis-cardinals";
                        break;
                    case "KC":
                        fullteam = "kansas-city-royals";
                        break;
                    case "DET":
                        fullteam = "detroit-tigers";
                        break;
                    case "TB":
                        fullteam = "tampa-bay-rays";
                        break;
                    case "SEA":
                        fullteam = "seattle-mariners";
                        break;
                    case "COL":
                        fullteam = "colorado-rockies";
                        break;
                    case "SD":
                        fullteam = "san-diego-padres";
                        break;
                    case "ARI":
                        fullteam = "";
                        break;
                    case "LAD":
                        fullteam = "los-angeles-dodgers";
                        break;
                    case "NYM":
                        fullteam = "new-york-mets";
                        break;
                    case "WSH":
                        fullteam = "washington-nationals";
                        break;
                    case "MIL":
                        fullteam = "milwaukee-brewers";
                        break;
                    case "TEX":
                        fullteam = "texas-rangers";
                        break;


                }
            }
            return fullteam;
        }
        public static string NFLConverter(string Team, bool TitleCase = false)
        {
            string fullteam = "";
            if (TitleCase)
            {
                switch (Team)
                {
                    case "Atlanta":
                        fullteam = "atlanta falcons";
                        break;
                    case "N.Y. Giants":
                        fullteam = "new york giants";
                        break;
                    case "Tennessee":
                        fullteam = "tennessee titans";
                        break;
                    case "L.A. Rams":
                        fullteam = "los angeles rams";
                        break;
                    case "New England":
                        fullteam = "new england patriots";
                        break;
                    case "Philadelphia":
                        fullteam = "philadelphia eagles";
                        break;
                    case "San Francisco":
                        fullteam = "san francisco 49ers";
                        break;
                    case "Miami":
                        fullteam = "miami dolphins";
                        break;
                    case "Houston":
                        fullteam = "houston texans";
                        break;
                    case "Baltimore":
                        fullteam = "baltimore ravens";
                        break;
                    case "Cleveland":
                        fullteam = "cleveland browns";
                        break;
                    case "Minnesota":
                        fullteam = "minnesota vikings";
                        break;
                    case "Chicago":
                        fullteam = "chicago bears";
                        break;
                    case "Cincinnati":
                        fullteam = "cincinnati bengals";
                        break;
                    case "Oakland":
                        fullteam = "oakland raiders";
                        break;
                    case "Buffalo":
                        fullteam = "buffalo bills";
                        break;
                    case "Pittsburgh":
                        fullteam = "pittsburgh steelers";
                        break;
                    case "Indianapolis":
                        fullteam = "indianapolis colts";
                        break;
                    case "Kansas City":
                        fullteam = "kansas city chiefs";
                        break;
                    case "Detriot":
                        fullteam = "detriot tigers";
                        break;
                    case "Tampa Bay":
                        fullteam = "tampa bay buccaneers";
                        break;
                    case "Seattle":
                        fullteam = "seattle seahawks";
                        break;
                    case "Denver":
                        fullteam = "denver broncos";
                        break;
                    case "L.A. Chargers":
                        fullteam = "los angeles chargers";
                        break;
                    case "Arizona":
                        fullteam = "arizona cardinals";
                        break;
                    case "New Orleans":
                        fullteam = "new orleans saints";
                        break;
                    case "N.Y. Jets":
                        fullteam = "new york jets";
                        break;
                    case "Washington":
                        fullteam = "washington redskins";
                        break;
                    case "Green Bay":
                        fullteam = "green bay packers";
                        break;
                    case "Dallas":
                        fullteam = "dallas cowboys";
                        break;
                    case "Carolina":
                        fullteam = "carolina panthers";
                        break;
                    case "Jacksonville":
                        fullteam = "jacksonville jaguars";
                        break;

                }
                fullteam = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(fullteam);
            }
            else
            {
                switch (Team)
                {
                    case "Atlanta":
                        fullteam = "atlanta-falcons";
                        break;
                    case "N.Y. Giants":
                        fullteam = "new-york-giants";
                        break;
                    case "Tennessee":
                        fullteam = "tennessee-titans";
                        break;
                    case "L.A. Rams":
                        fullteam = "los-angeles-rams";
                        break;
                    case "New England":
                        fullteam = "new-england-patriots";
                        break;
                    case "Philadelphia":
                        fullteam = "philadelphia-eagles";
                        break;
                    case "San Francisco":
                        fullteam = "san-francisco-49ers";
                        break;
                    case "Miami":
                        fullteam = "miami-dolphins";
                        break;
                    case "Houston":
                        fullteam = "houston-texans";
                        break;
                    case "Baltimore":
                        fullteam = "baltimore-ravens";
                        break;
                    case "Cleveland":
                        fullteam = "cleveland-browns";
                        break;
                    case "Minnesota":
                        fullteam = "minnesota-vikings";
                        break;
                    case "Chicago":
                        fullteam = "chicago-bears";
                        break;
                    case "Cincinnati":
                        fullteam = "cincinnati-bengals";
                        break;
                    case "Oakland":
                        fullteam = "oakland-raiders";
                        break;
                    case "Buffalo":
                        fullteam = "buffalo-bills";
                        break;
                    case "Pittsburgh":
                        fullteam = "pittsburgh-steelers";
                        break;
                    case "Indianapolis":
                        fullteam = "indianapolis-colts";
                        break;
                    case "Kansas City":
                        fullteam = "kansas-city-chiefs";
                        break;
                    case "Detriot":
                        fullteam = "detriot-tigers";
                        break;
                    case "Tampa Bay":
                        fullteam = "tampa-bay-buccaneers";
                        break;
                    case "Seattle":
                        fullteam = "seattle-seahawks";
                        break;
                    case "Denver":
                        fullteam = "denver-broncos";
                        break;
                    case "L.A. Chargers":
                        fullteam = "los-angeles-chargers";
                        break;
                    case "Arizona":
                        fullteam = "arizona-cardinals";
                        break;
                    case "New Orleans":
                        fullteam = "new-orleans-saints";
                        break;
                    case "N.Y. Jets":
                        fullteam = "new-york-jets";
                        break;
                    case "Washington":
                        fullteam = "washington-redskins";
                        break;
                    case "Green Bay":
                        fullteam = "green-bay-packers";
                        break;
                    case "Dallas":
                        fullteam = "dallas-cowboys";
                        break;
                    case "Carolina":
                        fullteam = "carolina-panthers";
                        break;
                    case "Jacksonville":
                        fullteam = "jacksonville-jaguars";
                        break;
                }
            }
            return fullteam;
        }
        #endregion
    }
}
