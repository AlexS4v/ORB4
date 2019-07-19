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
     * 
     * 
     * 
     * 
     * 
     */ 

    static class APIKeyGrabber
    {
        public static string GetAPIKey(string response)
        {
            int offset = response.IndexOf("API Key:") + "API Key:".Length+2;
            int index = 0;
            bool gotString = false;

            StringBuilder userCheck = new StringBuilder();

            while (true)
            {
                if (response[offset + index] == '<' || response[offset + index] == '>')
                {
                    if (gotString)
                    {
                        break;
                    }

                    gotString = true;
                    index++;
                    continue;
                }

                if (response[offset + index] == ' ') { index++; continue; } 

                if (gotString)
                    userCheck.Append(response[offset + index]);

                index++;
            }

            return userCheck.ToString();
        }

        public static string GetLocalUserCheck(string response)
        {
            int offset = response.IndexOf("localUserCheck") + "LocalUserCheck".Length;

            int index = 0;
            bool gotString = false;

            StringBuilder userCheck = new StringBuilder();

            while (true)
            {
                if (response[offset + index] == '"') {
                    if (gotString)
                    {
                        break;
                    }

                    gotString = true;
                    index++;
                    continue;
                }

                if (gotString)
                    userCheck.Append(response[offset + index]);

                index++;
            }

            return userCheck.ToString();
        }

        public static string Username = string.Empty;
        public static string Password = string.Empty;

        public static string APIKey = string.Empty;

        public static async Task Run(Action callback)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = true,
                    CookieContainer = new CookieContainer()
                };

                int retries = 10;

                HttpClient client = new HttpClient(handler);

                client.DefaultRequestHeaders.Add("user-agent", $"ORB ({Engine.Version})");
                StringContent content = new StringContent($"username={Username}&password={Password}&redirect=index.php&sid=&login=Login");
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");

                using (var loginResp = await client.PostAsync("https://osu.ppy.sh/forum/ucp.php?mode=login", content))
                {
                    string loginTextResp = await loginResp.Content.ReadAsStringAsync();
                    if (loginTextResp.Contains("You have specified an incorrect password.") || loginTextResp.Contains("You have specified an incorrect username."))
                    {
                        APIKey = "Error: Wrong username or password.";
                        callback();
                        return;
                    }
                }

                APIPageDL:

                if (retries > 0) {
                    using (var apiPageResp = await client.GetAsync("https://osu.ppy.sh/p/api"))
                    {
                        string apiPageTextResp = await apiPageResp.Content.ReadAsStringAsync();
                        if (apiPageTextResp.Contains("The title of your app which is requiring access to the API.") &&
                            apiPageTextResp.Contains("The URL of your app/site."))
                        {
                            string localUserCheck = GetLocalUserCheck(apiPageTextResp);
                            StringContent creationParams = new StringContent($"app_name=Osu!+Random+Beatmap&app_url=https%3A%2F%2Fgithub.com%2FAlexS4v%2FORB4&localUserCheck={localUserCheck}");
                            creationParams.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");

                            using (var apiRegisterResp = await client.PostAsync("https://osu.ppy.sh/p/api", creationParams))
                            {
                                string apiRegisterTextResp = await apiRegisterResp.Content.ReadAsStringAsync();
                                if (apiPageTextResp != "ok") { retries--; }
                            }

                            goto APIPageDL;
                        }
                        else if (apiPageTextResp.Contains("DO NOT GIVE THIS OUT TO OTHERS.") &&
                            apiPageTextResp.Contains("IT'S EQUIVALENT TO GIVING OUT YOUR PASSWORD.") &&
                            apiPageTextResp.Contains("YOUR ACCOUNT MAY BE COMPROMISED."))
                        {
                            string apiKey = GetAPIKey(apiPageTextResp);
                            if (apiKey.Length != 40)
                            {
                                retries--;
                                goto APIPageDL;
                            }
                            else
                            {
                                APIKey = apiKey;
                                callback();
                                return;
                            }
                        }
                        else
                        {
                            retries--;
                            goto APIPageDL;
                        }
                    }
                }

                APIKey = "Error: Too many retries.";
                callback();
                return;
            } catch (Exception e)
            {
                APIKey = "Error: Could not get the API Key due an unknown error";
                callback();
                return;
            }
        }

    }
}
