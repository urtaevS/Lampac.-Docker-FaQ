﻿using Microsoft.AspNetCore.Mvc;
using System;
using Merchant;
using IO = System.IO.File;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Chaos.NaCl;
using System.Text.RegularExpressions;
using System.Linq;
using System.Globalization;
using Microsoft.Extensions.Caching.Memory;

namespace Lampac.Controllers.LITE
{
    public class Streampay : MerchantController
    {
        static Streampay() { Directory.CreateDirectory("merchant/invoice/streampay"); }


        [HttpGet]
        [Route("streampay/new")]
        async public Task<ActionResult> Index(string email)
        {
            var init = AppInit.conf.Merchant.Streampay;
            if (!init.enable || string.IsNullOrWhiteSpace(email))
                return Content(string.Empty);

            email = decodeEmail(email);

            if (memoryCache.TryGetValue($"streampay:{email}", out string pay_link))
                return Redirect(pay_link);

            string transid = DateTime.Now.ToBinary().ToString().Replace("-", "");
            IO.WriteAllText($"merchant/invoice/streampay/{transid}", email);

            var body = new
            {
                init.store_id,
                external_id = $"{email}_{transid}",
                description = $"Подписка на {AppInit.conf.Merchant.accessForMonths} {EndOfText("месяц", "месяца", "месяцев", AppInit.conf.Merchant.accessForMonths)}",
                system_currency = "USDT",
                payment_type = 2,
                amount = AppInit.conf.Merchant.accessCost
            };

            var jsonBody = JsonSerializer.Serialize(body);
            string signature = Sign(Encoding.UTF8.GetBytes(jsonBody + DateTime.UtcNow.ToString("yyyyMMdd:HHmm")), init.private_key);

            using (var httpClient = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.streampay.org/api/payment/create")
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };

                request.Headers.Add("signature", signature);

                var response = await httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string respData = await response.Content.ReadAsStringAsync();
                    string pay_url = Regex.Match(respData, "\"pay_url\":\"([^\"]+)\"").Groups[1].Value;

                    if (!string.IsNullOrEmpty(pay_url))
                    {
                        memoryCache.Set($"streampay:{email}", pay_url, DateTime.Now.AddHours(2));
                        return Redirect(pay_url);
                    }

                    return Content(respData);
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return Content("Invalid signature");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotAcceptable)
                {
                    return Content("Invalid request data");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                {
                    return Content("Internal server error");
                }
            }

            return Content("error");
        }


        [HttpGet]
        [Route("streampay/callback")]
        public ActionResult Callback()
        {
            string[] externals = Request.Query["external_id"].ToString().Split("_");
            string external_id = externals[1];

            if (Request.Query["status"] != "awaiting_payment")
                memoryCache.Remove($"streampay:{externals[0]}");

            var merchant = AppInit.conf.Merchant;
            if (!merchant.Streampay.enable || !IO.Exists($"merchant/invoice/streampay/{external_id}") || Request.Query["status"] != "success")
                return Ok();

            var now = DateTime.UtcNow;
            var queryParams = Request.Query.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}").ToList();
            string paramsStr = string.Join('&', queryParams);
            byte[] paramsBuf = Encoding.UTF8.GetBytes(paramsStr);

            string log = $"{paramsStr}\n{JsonSerializer.Serialize(Request.Headers)}";

            for (int i = 0; i < 2; i++)
            {
                string tm = now.ToString("yyyyMMdd:HHmm");
                var bufToSign = paramsBuf.Concat(Encoding.UTF8.GetBytes(tm)).ToArray();

                bool verify = Verify(Request.Headers["Signature"], bufToSign, merchant.Streampay.public_key);
                log += $"\nverify: {verify} | {tm} | signature: {Request.Headers["Signature"]}";

                if (verify)
                {
                    string email = IO.ReadAllText($"merchant/invoice/streampay/{external_id}");
                    double.TryParse(Request.Query["amount"], NumberStyles.Float, CultureInfo.InvariantCulture, out double amount);

                    if (amount > 0 && (merchant.accessCost > (amount + merchant.allowedDifference) || merchant.accessCost < (amount - merchant.allowedDifference)))
                    {
                        double cost = (double)merchant.accessCost / (double)(merchant.accessForMonths * 30);
                        PayConfirm(email, "streampay", external_id, days: (int)(amount / cost));
                    }
                    else
                    {
                        PayConfirm(email, "streampay", external_id);
                    }

                    WriteLog("streampay", log + "\nOK");
                    return Ok();
                }

                now = now.AddMinutes(-1);
            }

            WriteLog("streampay", log + "\nForbid");
            return Forbid();
        }



        static string Sign(byte[] message, string privateKey)
        {
            var bytes = Ed25519.Sign(message, HexToBytes(privateKey));

            StringBuilder hex = new StringBuilder(bytes.Length * 2);

            foreach (byte b in bytes)
                hex.AppendFormat("{0:x2}", b);

            return hex.ToString();
        }

        static bool Verify(string signature, byte[] message, string publicKey)
        {
            try
            {
                return Ed25519.Verify(HexToBytes(signature), message, HexToBytes(publicKey));
            }
            catch { return false; }
        }

        static byte[] HexToBytes(string key)
        {
            if (key.Length % 2 != 0)
                return null;

            int byteCount = key.Length / 2;
            byte[] hexKey = new byte[byteCount];

            for (int i = 0; i < byteCount; i++)
            {
                string byteString = key.Substring(i * 2, 2);
                byte keyValue = Convert.ToByte(byteString, 16);
                hexKey[i] = keyValue;
            }

            return hexKey;
        }

        static string EndOfText(string s1, string s2, string s3, int x)
        {
            int n = x % 100;
            if ((n > 10) && (n < 20))
                return s3;

            switch (x % 10)
            {
                case 4: return s2;
                case 0:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9: return s3;
                default: return s1;
            }
        }
    }
}
