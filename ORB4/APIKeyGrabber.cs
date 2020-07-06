using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;

namespace ORB4
{
    /*
    *     Decompiled code, sorry for the incovenience.  ^^
    */

    internal static class APIKeyGrabber
    {
        public static string Username = string.Empty;
        public static string Password = string.Empty;
        public static string APIKey = string.Empty;

        public static string GetAPIKey(string response)
        {
            int num1 = response.IndexOf("API Key:") + "API Key:".Length + 2;
            int num2 = 0;
            bool flag = false;
            StringBuilder stringBuilder = new StringBuilder();
            while (true)
            {
                if (response[num1 + num2] == '<' || response[num1 + num2] == '>')
                {
                    if (!flag)
                    {
                        flag = true;
                        ++num2;
                    }
                    else
                        break;
                }
                else if (response[num1 + num2] == ' ')
                {
                    ++num2;
                }
                else
                {
                    if (flag)
                        stringBuilder.Append(response[num1 + num2]);
                    ++num2;
                }
            }
            return stringBuilder.ToString();
        }

        public static string GetLocalUserCheck(string response)
        {
            int num1 = response.IndexOf("localUserCheck") + "LocalUserCheck".Length;
            int num2 = 0;
            bool flag = false;
            StringBuilder stringBuilder = new StringBuilder();
            while (true)
            {
                if (response[num1 + num2] == '"')
                {
                    if (!flag)
                    {
                        flag = true;
                        ++num2;
                    }
                    else
                        break;
                }
                else
                {
                    if (flag)
                        stringBuilder.Append(response[num1 + num2]);
                    ++num2;
                }
            }
            return stringBuilder.ToString();
        }

        public static async Task Run(Action callback)
        {
            try
            {
                HttpClientHandler handler = new HttpClientHandler()
                {
                    AllowAutoRedirect = false,
                    UseCookies = true,
                    CookieContainer = new CookieContainer()
                };
                int retries = 10;
                HttpClient client = new HttpClient((HttpMessageHandler)handler);
                client.DefaultRequestHeaders.Add("user-agent", "ORB (4.2.6S)");
                client.DefaultRequestHeaders.Add("referer", " https://osu.ppy.sh/forum/ucp.php?mode=login");
                HttpResponseMessage async = await client.GetAsync("https://osu.ppy.sh/forum/ucp.php?mode=login");
                StringContent content = new StringContent(("username=" + APIKeyGrabber.Username + "&password=" + APIKeyGrabber.Password + "&redirect=index.php&sid=&login=Login").Replace(' ', '+'));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                content.Headers.Add("origin", "https://osu.ppy.sh");
                content.Headers.Add("sec-fetch-mode", "navigate");
                content.Headers.Add("sec-fetch-site", "same-origin");
                content.Headers.Add("sec-fetch-user", "?1");
                content.Headers.Add("upgrade-insecure-requests", "1");
                using (HttpResponseMessage loginResp = await client.PostAsync("https://osu.ppy.sh/forum/ucp.php?mode=login", (HttpContent)content))
                {
                    string loginTextResp = await loginResp.Content.ReadAsStringAsync();
                    if (loginTextResp.Contains("You have specified an incorrect password.") || loginTextResp.Contains("You have specified an incorrect username."))
                    {
                        APIKeyGrabber.APIKey = "Error: Wrong username or password.";
                        callback();
                        return;
                    }
                    loginTextResp = (string)null;
                }
                while (retries > 0)
                {
                    using (HttpResponseMessage apiPageResp = await client.GetAsync("https://osu.ppy.sh/p/api"))
                    {
                        string apiPageTextResp = await apiPageResp.Content.ReadAsStringAsync();
                        if (apiPageTextResp.Contains("The title of your app which is requiring access to the API.") && apiPageTextResp.Contains("The URL of your app/site."))
                        {
                            string localUserCheck = APIKeyGrabber.GetLocalUserCheck(apiPageTextResp);
                            StringContent creationParams = new StringContent("app_name=Osu!+Random+Beatmap&app_url=https%3A%2F%2Fgithub.com%2FAlexS4v%2FORB4&localUserCheck=" + localUserCheck);
                            creationParams.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                            using (HttpResponseMessage apiRegisterResp = await client.PostAsync("https://osu.ppy.sh/p/api", (HttpContent)creationParams))
                            {
                                string apiRegisterTextResp = await apiRegisterResp.Content.ReadAsStringAsync();
                                if (apiPageTextResp != "ok")
                                    --retries;
                                apiRegisterTextResp = (string)null;
                            }
                        }
                        else if (apiPageTextResp.Contains("DO NOT GIVE THIS OUT TO OTHERS.") && apiPageTextResp.Contains("IT'S EQUIVALENT TO GIVING OUT YOUR PASSWORD.") && apiPageTextResp.Contains("YOUR ACCOUNT MAY BE COMPROMISED."))
                        {
                            string apiKey = APIKeyGrabber.GetAPIKey(apiPageTextResp);
                            if (apiKey.Length != 40)
                            {
                                --retries;
                            }
                            else
                            {
                                APIKeyGrabber.APIKey = apiKey;
                                callback();
                                return;
                            }
                        }
                        else
                            --retries;
                    }
                }
                APIKeyGrabber.APIKey = "Error: Too many retries.";
                callback();
            }
            catch (Exception ex)
            {
                Exception e = ex;
                APIKeyGrabber.APIKey = "Error: Could not get the API Key due an unknown error";
                callback();
            }
        }
    }
}
