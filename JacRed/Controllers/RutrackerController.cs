﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Lampac.Engine.CORE;
using Lampac.Engine.Parse;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using Shared;
using JacRed.Engine;
using JacRed.Models;

namespace Lampac.Controllers.JAC
{
    [Route("rutracker/[action]")]
    public class RutrackerController : JacBaseController
    {
        #region Cookie / TakeLogin
        static string Cookie;

        async static ValueTask<bool> TakeLogin()
        {
            string authKey = "rutracker:TakeLogin()";
            if (Startup.memoryCache.TryGetValue(authKey, out _))
                return false;

            Startup.memoryCache.Set(authKey, 0, AppInit.conf.multiaccess ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(20));

            try
            {
                var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false
                };

                clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                using (var client = new System.Net.Http.HttpClient(clientHandler))
                {
                    client.Timeout = TimeSpan.FromSeconds(jackett.timeoutSeconds);
                    client.MaxResponseContentBufferSize = 2000000; // 2MB
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");

                    var postParams = new Dictionary<string, string>
                    {
                        { "login_username", jackett.Rutracker.login.u },
                        { "login_password", jackett.Rutracker.login.p },
                        { "login", "Вход" }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"{jackett.Rutracker.host}/forum/login.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string session = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("bb_session="))
                                        session = new Regex("bb_session=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(session))
                                {
                                    Cookie = $"bb_ssl=1; bb_session={session};";
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }
        #endregion

        #region parseMagnet
        async public Task<ActionResult> parseMagnet(string id)
        {
            if (!jackett.Rutracker.enable)
                return Content("disable");

            #region кеш / cookie
            string keydownload = $"rutracker:parseMagnet:download:{id}";
            if (Startup.memoryCache.TryGetValue(keydownload, out byte[] _t))
                return File(_t, "application/x-bittorrent");

            string key = $"rutracker:parseMagnet:{id}";
            if (Startup.memoryCache.TryGetValue(key, out string _m))
                return Redirect(_m);
            #endregion

            #region emptycache
            string keyerror = $"rutracker:parseMagnet:{id}:error";
            if (Startup.memoryCache.TryGetValue(keyerror, out _))
            {
                if (TorrentCache.Read(keydownload) is var tcache && tcache.cache)
                    return File(tcache.torrent, "application/x-bittorrent");

                if (TorrentCache.ReadMagnet(key) is var mcache && mcache.cache)
                    Redirect(mcache.torrent);

                return Content("error");
            }
            #endregion

            #region TakeLogin
            if (Cookie == null && await TakeLogin() == false)
            {
                if (TorrentCache.Read(keydownload) is var tcache && tcache.cache)
                    return File(tcache.torrent, "application/x-bittorrent");

                if (TorrentCache.ReadMagnet(key) is var mcache && mcache.cache)
                    Redirect(mcache.torrent);

                return Content("TakeLogin == false");
            }
            #endregion

            #region Download
            if (jackett.Rutracker.priority == "torrent")
            {
                _t = await HttpClient.Download($"{jackett.Rutracker.host}/forum/dl.php?t={id}", cookie: Cookie, referer: jackett.Rutracker.host, timeoutSeconds: 10);
                if (_t != null && BencodeTo.Magnet(_t) != null)
                {
                    if (jackett.cache)
                    {
                        TorrentCache.Write(keydownload, _t);
                        Startup.memoryCache.Set(keydownload, _t, DateTime.Now.AddMinutes(Math.Max(1, jackett.torrentCacheToMinutes)));
                    }

                    return File(_t, "application/x-bittorrent");
                }
            }
            #endregion

            #region Magnet
            var fullNews = await HttpClient.Get($"{jackett.Rutracker.host}/forum/viewtopic.php?t=" + id, cookie: Cookie, timeoutSeconds: 10);
            if (fullNews != null)
            {
                string magnet = Regex.Match(fullNews, "href=\"(magnet:[^\"]+)\" class=\"(med )?med magnet-link\"").Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(magnet))
                {
                    if (jackett.cache)
                    {
                        TorrentCache.Write(key, magnet);
                        Startup.memoryCache.Set(key, magnet, DateTime.Now.AddMinutes(Math.Max(1, jackett.torrentCacheToMinutes)));
                    }

                    return Redirect(magnet);
                }
            }
            #endregion

            if (jackett.emptycache && jackett.cache)
                Startup.memoryCache.Set(keyerror, 0, DateTime.Now.AddMinutes(1));

            if (jackett.cache)
            {
                if (TorrentCache.Read(keydownload) is var tcache && tcache.cache)
                    return File(tcache.torrent, "application/x-bittorrent");

                if (TorrentCache.ReadMagnet(key) is var mcache && mcache.cache)
                    Redirect(mcache.torrent);
            }

            return Content("error");
        }
        #endregion


        #region search
        public static Task<bool> search(string host, ConcurrentBag<TorrentDetails> torrents, string query, string[] cats)
        {
            if (!jackett.Rutracker.enable)
                return Task.FromResult(false);

            return JackettCache.Invoke($"rutracker:{string.Join(":", cats ?? new string[] { })}:{query}", torrents, () => parsePage(host, query, cats));
        }
        #endregion

        #region parsePage
        async static ValueTask<List<TorrentDetails>> parsePage(string host, string query, string[] cats)
        {
            var torrents = new List<TorrentDetails>();

            #region Авторизация
            if (Cookie == null)
            {
                if (await TakeLogin() == false)
                    return null;
            }
            #endregion

            #region Кеш html
            bool firstrehtml = true;
            rehtml: string html = await HttpClient.Get($"{jackett.Rutracker.host}/forum/tracker.php?nm=" + HttpUtility.UrlEncode(query), cookie: Cookie, timeoutSeconds: jackett.timeoutSeconds);

            if (html != null)
            {
                if (!html.Contains("id=\"logged-in-username\""))
                {
                    if (!firstrehtml || await TakeLogin() == false)
                        return null;

                    firstrehtml = false;
                    goto rehtml;
                }
            }
            #endregion

            foreach (string row in html.Split("class=\"tCenter hl-tr\"").Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row))
                    continue;

                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                #region Данные раздачи
                string title = Match("href=\"viewtopic.php\\?t=[0-9]+\">([^\n\r]+)</a>");
                title = Regex.Replace(title, "<[^>]+>", "");

                DateTime createTime = tParse.ParseCreateTime(Match("<p>([0-9]{2}-[^-<]+-[0-9]{2})</p>").Replace("-", " "), "dd.MM.yy");
                string viewtopic = Match("href=\"viewtopic.php\\?t=([0-9]+)\"");
                string tracker = Match("href=\"tracker.php\\?f=([0-9]+)");
                string _sid = Match("class=\"seedmed\">([0-9]+)</b>");
                string _pir = Match("title=\"Личи\">([0-9]+)</td>");
                string sizeName = Match("href=\"dl.php\\?t=[0-9]+\">([^<]+) &#8595;</a>").Replace("&nbsp;", " ");

                if (string.IsNullOrWhiteSpace(viewtopic) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(tracker))
                    continue;

                string url = $"{jackett.Rutracker.host}/forum/viewtopic.php?t={viewtopic}";
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (tracker is "22" or "1666" or "941" or "252" or "1950" or "1950" or "2090" or "2221" or "2091" or "2092" or "2093" or "2200" or "2540" or "934" or "505" or "124" or "1457"
                                or "2199" or "313" or "312" or "1247" or "2201" or "2339" or "140" or "2343" or "930" or "2365" or "208" or "539" or "209" or "709")
                {
                    #region Фильмы
                    // Ниже нуля / Bajocero / Below Zero (Йуис Килес / Lluís Quílez) [2021, Испания, боевик, триллер, криминал, WEB-DLRip] MVO (MUZOBOZ) + Original (Spa) + Sub (Rus, Eng)
                    var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Белый тигр / The White Tiger (Рамин Бахрани / Ramin Bahrani) [2021, Индия, США, драма, криминал, WEB-DLRip] MVO (HDRezka Studio) + Sub (Rus, Eng) + Original Eng
                        g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Дневной дозор (Тимур Бекмамбетов) [2006, Россия, боевик, триллер, фэнтези, BDRip-AVC]
                            g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                            {
                                name = g[1].Value;
                                if (int.TryParse(g[2].Value, out int _yer))
                                    relased = _yer;
                            }
                        }
                    }
                    #endregion
                }
                else if (tracker is "842" or "235" or "242" or "819" or "1531" or "721" or "1102" or "1120" or "1214" or "489" or "387" or "9" or "81" or "119" or "1803" or "266" or "193" or "1690" or "1459" or "825" or "1248" or "1288"
                                      or "325" or "534" or "694" or "704" or "921" or "815" or "1460")
                {
                    #region Сериалы
                    if (!Regex.IsMatch(title, "(Сезон|Серии)", RegexOptions.IgnoreCase))
                        continue;

                    if (title.Contains("Сезон:"))
                    {
                        // Голяк / Без гроша / Без денег / Brassic / Сезон: 4 / Серии: 1-8 из 8 (Джон Райт, Дэниэл О’Хара, Сауль Метцштайн, Джон Хардвик) [2022, Великобритания, Комедия, криминал, WEB-DLRip] MVO (Ozz) + Original + Sub (Rus, Ukr, Eng)
                        var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / [^/\\(\\[]+ / ([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Уравнитель / Великий уравнитель / The Equalizer / Сезон: 1 / Серии: 1-3 из 4 (Лиз Фридлендер, Солван Наим) [2021, США, Боевик, триллер, драма, криминал, детектив, WEB-DLRip] MVO (TVShows) + Original
                            g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // 911 служба спасения / 9-1-1 / Сезон: 4 / Серии: 1-6 из 9 (Брэдли Букер, Дженнифер Линч, Гвинет Хердер-Пэйтон) [2021, США, Боевик, триллер, драма, WEB-DLRip] MVO (LostFilm) + Original
                                g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                {
                                    name = g[1].Value;
                                    originalname = g[2].Value;

                                    if (int.TryParse(g[3].Value, out int _yer))
                                        relased = _yer;
                                }
                                else
                                {
                                    // Петербургский роман / Сезон: 1 / Серии: 1-8 из 8 (Александр Муратов) [2018, мелодрама, HDTV 1080i]
                                    g = Regex.Match(title, "^([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                    {
                                        name = g[1].Value;
                                        if (int.TryParse(g[2].Value, out int _yer))
                                            relased = _yer;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Уравнитель / Великий уравнитель / The Equalizer / Серии: 1-3 из 4 (Лиз Фридлендер, Солван Наим) [2021, США, Боевик, триллер, драма, криминал, детектив, WEB-DLRip] MVO (TVShows) + Original
                        var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // 911 служба спасения / 9-1-1 / Серии: 1-6 из 9 (Брэдли Букер, Дженнифер Линч, Гвинет Хердер-Пэйтон) [2021, США, Боевик, триллер, драма, WEB-DLRip] MVO (LostFilm) + Original
                            g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Петербургский роман / Серии: 1-8 из 8 (Александр Муратов) [2018, мелодрама, HDTV 1080i]
                                g = Regex.Match(title, "^([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                {
                                    name = g[1].Value;
                                    if (int.TryParse(g[2].Value, out int _yer))
                                        relased = _yer;
                                }
                            }
                        }
                    }

                    if (Regex.IsMatch(name ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase) || Regex.IsMatch(originalname ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase))
                        continue;
                    #endregion
                }
                else if (tracker is "1105" or "2491" or "1389" or "915" or "1939" or "46" or "671" or "2177" or "2538" or "251" or "98" or "97" or "851" or "2178" or "821" or "2076" or "56" or "2123" or "876" or "2139" or "1467"
                                       or "1469" or "249" or "552" or "500" or "2112" or "1327" or "1468" or "2168" or "2160" or "314" or "1281" or "2110" or "979" or "2169" or "2164" or "2166" or "2163"
                                       or "24" or "1959" or "939" or "1481" or "113" or "115" or "882" or "1482" or "393" or "2537" or "532" or "827")
                {
                    #region Нестандартные титлы
                    name = Regex.Match(title, "^([^/\\(\\[]+) ").Groups[1].Value;

                    if (int.TryParse(Regex.Match(title, " \\[([0-9]{4})(,|-) ").Groups[1].Value, out int _yer))
                        relased = _yer;

                    if (Regex.IsMatch(name ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase))
                        continue;
                    #endregion
                }
                #endregion

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name) || cats == null)
                {
                    #region types
                    string[] types = null;
                    switch (tracker)
                    {
                        case "22":
                        case "1666":
                        case "941":
                        case "1950":
                        case "2090":
                        case "2221":
                        case "2091":
                        case "2092":
                        case "2093":
                        case "2200":
                        case "2540":
                        case "934":
                        case "505":
                        case "124":
                        case "1457":
                        case "2199":
                        case "313":
                        case "312":
                        case "1247":
                        case "2201":
                        case "2339":
                        case "140":
                        case "252":
                            types = new string[] { "movie" };
                            break;
                        case "2343":
                        case "930":
                        case "2365":
                        case "208":
                        case "539":
                        case "209":
                            types = new string[] { "multfilm" };
                            break;
                        case "921":
                        case "815":
                        case "1460":
                            types = new string[] { "multserial" };
                            break;
                        case "842":
                        case "235":
                        case "242":
                        case "819":
                        case "1531":
                        case "721":
                        case "1102":
                        case "1120":
                        case "1214":
                        case "489":
                        case "387":
                        case "9":
                        case "81":
                        case "119":
                        case "1803":
                        case "266":
                        case "193":
                        case "1690":
                        case "1459":
                        case "825":
                        case "1248":
                        case "1288":
                        case "325":
                        case "534":
                        case "694":
                        case "704":
                        case "915":
                        case "1939":
                            types = new string[] { "serial" };
                            break;
                        case "1105":
                        case "2491":
                        case "1389":
                            types = new string[] { "anime" };
                            break;
                        case "709":
                            types = new string[] { "documovie" };
                            break;
                        case "46":
                        case "671":
                        case "2177":
                        case "2538":
                        case "251":
                        case "98":
                        case "97":
                        case "851":
                        case "2178":
                        case "821":
                        case "2076":
                        case "56":
                        case "2123":
                        case "876":
                        case "2139":
                        case "1467":
                        case "1469":
                        case "249":
                        case "552":
                        case "500":
                        case "2112":
                        case "1327":
                        case "1468":
                        case "2168":
                        case "2160":
                        case "314":
                        case "1281":
                        case "2110":
                        case "979":
                        case "2169":
                        case "2164":
                        case "2166":
                        case "2163":
                            types = new string[] { "docuserial", "documovie" };
                            break;
                        case "24":
                        case "1959":
                        case "939":
                        case "1481":
                        case "113":
                        case "115":
                        case "882":
                        case "1482":
                        case "393":
                        case "2537":
                        case "532":
                        case "827":
                            types = new string[] { "tvshow" };
                            break;
                        case "2103":
                        case "2522":
                        case "2485":
                        case "2486":
                        case "2479":
                        case "2089":
                        case "1794":
                        case "845":
                        case "2312":
                        case "343":
                        case "2111":
                        case "1527":
                        case "2069":
                        case "1323":
                        case "2009":
                        case "2000":
                        case "2010":
                        case "2006":
                        case "2007":
                        case "2005":
                        case "259":
                        case "2004":
                        case "1999":
                        case "2001":
                        case "2002":
                        case "283":
                        case "1997":
                        case "2003":
                        case "1608":
                        case "1609":
                        case "2294":
                        case "1229":
                        case "1693":
                        case "2532":
                        case "136":
                        case "592":
                        case "2533":
                        case "1952":
                        case "1621":
                        case "2075":
                        case "1668":
                        case "1613":
                        case "1614":
                        case "1623":
                        case "1615":
                        case "1630":
                        case "2425":
                        case "2514":
                        case "1616":
                        case "2014":
                        case "1442":
                        case "1491":
                        case "1987":
                        case "1617":
                        case "1620":
                        case "1998":
                        case "1343":
                        case "751":
                        case "1697":
                        case "255":
                        case "260":
                        case "261":
                        case "256":
                        case "1986":
                        case "660":
                        case "1551":
                        case "626":
                        case "262":
                        case "1326":
                        case "978":
                        case "1287":
                        case "1188":
                        case "1667":
                        case "1675":
                        case "257":
                        case "875":
                        case "263":
                        case "2073":
                        case "550":
                        case "2124":
                        case "1470":
                        case "528":
                        case "486":
                        case "854":
                        case "2079":
                        case "1336":
                        case "2171":
                        case "1339":
                        case "2455":
                        case "1434":
                        case "2350":
                        case "1472":
                        case "2068":
                        case "2016":
                            types = new string[] { "sport" };
                            break;
                    }

                    if (types == null)
                        continue;

                    if (cats != null)
                    {
                        bool isok = false;
                        foreach (string cat in cats)
                        {
                            if (types.Contains(cat))
                                isok = true;
                        }

                        if (!isok)
                            continue;
                    }
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = "rutracker",
                        types = types,
                        url = url,
                        title = title,
                        sid = sid,
                        pir = pir,
                        sizeName = sizeName,
                        createTime = createTime,
                        parselink = $"{host}/rutracker/parsemagnet?id={viewtopic}",
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
            }

            return torrents;
        }
        #endregion
    }
}
