using HtmlAgilityPack;
using Microsoft.Azure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RedditSharp;
using RedditSharp.Things;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace rJacksonvilleModBot
{
    class Program
    {
        private const string GoogleMapsVenueUrl = "http://maps.google.com/?q={0}+{1}+{2}+{3}";
        private const string DailyEventsUrl = "https://api.eviesays.com/1.1/getEvents.json?api_key=082e2a38281a9410f656b39ca67ee8a809db40d6&latitude=30.3321838&longitude=-81.655651&time_zone=America/New_York&start_date={0}-{1}-{2}&end_date={0}-{1}-{3}&limit=500&request={{%22params%22:{{%22order_by%22:[%22start_time%20asc%22],%22status%22:%22approved%22,%22current_site_id%22:2435}}}}";
        private const string DailyPostDescription = "Know of an event on {0}? Post it here and when the date comes around, it'll be linked to from the sidebar.";
        private const string DailyPostTitleFormat = "Jacksonville Events Calendar: {1} {2}, {0}";
        private const string SidebarSectionMarkdown = "#**Events and Entertainment**";
        private const string SidebarSectionAdditional = "\r\n**More event & entertainment resources**\r\n\r\n* [Downtown Jacksonville](http://downtownjacksonville.org/Downtown_Vision_Inc_Home.aspx)\r\n* [Jacksonville.com Calendar](http://www.jacksonville.com/calendar)\r\n* [JaxEvents](http://www.jaxevents.com/)\r\n* [Folio Events](http://folioweekly.com/calendar)\r\n* [Jax4Kids - Family Friendly Events](http://jax4kids.com/)";
        private const string SubredditName = "/r/Jacksonville";
        private static HttpClient httpClient = new HttpClient();

        private static async Task CreateDailyEvents(Post post, int year, int month, int day)
        {
            var url = string.Format(DailyEventsUrl, year, month, day, day + 1);

            var response = await httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                var events = json["events"];

                foreach (var @event in events)
                {
                    var eventComment = string.Empty;

                    var title = @event["title"]?.ToString();
                    var eventUrl = @event["url"]?.ToString();
                    var description = @event["description"]?.ToString();
                    var flyerUrls = @event["image_urls"]?.SelectTokens("$..detail_2x");
                    var times = @event["times"]?.SelectTokens("$..time_describe");
                    var venue = @event["venue"];

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        eventComment += $"**Event**:" + Environment.NewLine + Environment.NewLine;
                        eventComment += $"[{title}]({eventUrl}) {string.Join(" ", flyerUrls.Select(flyerUrl => $"[🖼️]({flyerUrl})"))}" + Environment.NewLine + Environment.NewLine;
                    }

                    if (times != null)
                    {
                        eventComment += $"**Time**:" + Environment.NewLine + Environment.NewLine;
                        eventComment += $"{string.Join("; ", times.Select(time => time.ToString()))} " + Environment.NewLine + Environment.NewLine;
                    }

                    if (venue != null)
                    {
                        var venueName = venue["title"]?.ToString();
                        var address1 = venue["address"]?.ToString();
                        var address2 = venue["address_2"]?.ToString();
                        var city = venue["city"]?.ToString();
                        var state = venue["state"]?.ToString();
                        var zipCode = venue["postal_code"]?.ToString();
                        var venueUrl = venue.SelectToken("data.url")?.ToString();

                        if (!string.IsNullOrWhiteSpace(venueUrl))
                            venueUrl = venueUrl.StartsWith("http") ? venueUrl : "http://" + venueUrl;

                        eventComment += "**Venue**:" + Environment.NewLine + Environment.NewLine;

                        if (string.IsNullOrWhiteSpace(venueUrl))
                            eventComment += $"{venueName} ";
                        else
                            eventComment += $"[{venueName}]({venueUrl}) ";

                        eventComment += $"[📍]({string.Format(GoogleMapsVenueUrl, venueName, address1, city, state)})" + Environment.NewLine + Environment.NewLine;
                        eventComment += address1 + Environment.NewLine + Environment.NewLine;
                        eventComment += address2 + Environment.NewLine + Environment.NewLine;
                        eventComment += $"{city}, {state} {zipCode}" + Environment.NewLine + Environment.NewLine;
                    }

                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        eventComment += "**Description**:" + Environment.NewLine + Environment.NewLine;
                        eventComment += description.Replace("\r", Environment.NewLine + Environment.NewLine).Replace("\n", Environment.NewLine + Environment.NewLine) + Environment.NewLine + Environment.NewLine;
                    }

                    if (!string.IsNullOrWhiteSpace(eventComment))
                    {
                        eventComment += "&nbsp;" + Environment.NewLine + Environment.NewLine + "^^source: ^^[Jacksonville.com&nbsp;Calendar](http://www.jacksonville.com/calendar)";

                        post.Comment(eventComment);
                    }

                    Thread.Sleep(1000); // Wait 1 second(s) before posting another comment
                }
            }
        }

        private static IEnumerable<Post> GetOrCreateDailyPosts(Reddit reddit, Subreddit subreddit, AuthenticatedUser user, int year, int month)
        {
            var firstDateOfMonth = new DateTime(year, month, 1);
            var monthName = firstDateOfMonth.ToString("MMMM");

            var createdPost = false;
            var dailyPostDate = firstDateOfMonth;
            var userPosts = user.GetPosts(Sort.New, 40, FromTime.Year).ToList();

            while (dailyPostDate.Month == month)
            {
                var dailyPostTitle = string.Format(DailyPostTitleFormat, year, monthName, dailyPostDate.Day);
                var dailyPosts = userPosts.Where(p => p.Title == dailyPostTitle).OrderBy(p => p.Created);

                if (dailyPosts.Count() == 1)
                {
                    yield return dailyPosts.First();
                }
                else
                {
                    // Somehow we got multiple posts for the same day. Drop and then recreate them.
                    if (dailyPosts.Count() > 1)
                    {
                        Console.WriteLine("Multiple posts found for " + dailyPostDate.ToShortDateString() + " post." + Environment.NewLine + string.Join(Environment.NewLine, dailyPosts.Select(p => p.Shortlink)));

                        // Drop any existing posts. This can occur during a partial run or error event.
                        foreach (var dailyPost in dailyPosts)
                            dailyPost.Del();
                    }

                    // Create the post for the day.
                    var post = subreddit.SubmitTextPost(dailyPostTitle, string.Format(DailyPostDescription, dailyPostDate.ToLongDateString()));

                    CreateDailyEvents(post, dailyPostDate.Year, dailyPostDate.Month, dailyPostDate.Day).Wait();

                    Console.WriteLine($"Created daily post {dailyPostTitle}");

                    yield return post;

                    createdPost = true;
                }

                dailyPostDate = dailyPostDate.AddDays(1);
            }

            if (createdPost)
                reddit.ComposePrivateMessage("New month worth of posts created", $"Daily posts were created for {monthName}.", SubredditName);
        }

        static void Main(string[] args)
        {
            var password = string.Empty;
            var username = string.Empty;

            if (args != null && args.Length == 2)
            {
                password = args[1];
                username = args[0];
            }
            else
            {
                password = CloudConfigurationManager.GetSetting("pass");
                username = CloudConfigurationManager.GetSetting("user");
            }

            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(username))
            {
                Console.WriteLine("Invalid or missing username and password.");
                Environment.Exit(1);
            }

            var reddit = new Reddit();
            var user = reddit.LogIn(username, password);
            var subreddit = reddit.GetSubreddit(SubredditName);

            AppDomain.CurrentDomain.UnhandledException += (o, e) =>
            {
                reddit.ComposePrivateMessage("Exception", e.ExceptionObject.ToString(), SubredditName);
                Console.WriteLine(e.ExceptionObject);
                Environment.Exit(3);
            };

            subreddit.Subscribe();

            if (!user.ModeratorSubreddits.Any(s => s.ToString().Equals(SubredditName, StringComparison.OrdinalIgnoreCase)))
            {
                reddit.ComposePrivateMessage("Bot user not a moderator", "The user '" + username + "' is not a moderator for the " + SubredditName + " subreddit.", SubredditName);
                Console.WriteLine("The user '" + username + "' is not a moderator for the " + SubredditName + " subreddit.");
                Environment.Exit(2);
            }

            // Reply to any private messages that have been sent to the mod bot.
            if (user.UnreadMessages.Any())
            {
                foreach (var message in user.PrivateMessages.Where(m => m.Unread))
                {
                    message.Reply("You have messaged the " + SubredditName + " moderator bot. These private messages are not actively monitored.");
                    message.SetAsRead();
                }
            }

            // Get or create the post for this month
            var today = DateTime.Now;
            var dailyPosts = GetOrCreateDailyPosts(reddit, subreddit, user, today.Year, today.Month).ToList();
            var todaysPosts = dailyPosts.Where(p => p.AuthorName == user.Name && p.Title == string.Format(DailyPostTitleFormat, today.Year, today.ToString("MMMM"), today.Day)).ToList();

            if (dailyPosts.Any() && todaysPosts.Any())
            {
                var settings = subreddit.Settings;
                var sidebar = settings.Sidebar;

                if (!string.IsNullOrEmpty(sidebar) && sidebar.Contains(SidebarSectionMarkdown))
                {
                    var startIndex = sidebar.IndexOf(SidebarSectionMarkdown, StringComparison.Ordinal) + SidebarSectionMarkdown.Length;
                    var endIndex = sidebar.IndexOf("#**", startIndex, StringComparison.Ordinal); // Find the beginning of the next section 

                    if (endIndex < 0)
                        endIndex = sidebar.Length - 1; // There's no next section, so just replace the rest of the content

                    var newSidebarContent = "* [" + today.DayOfWeek + "](" + todaysPosts.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + "* [" + today.ToString("MMMM") + "](" + todaysPosts.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + "* [" + today.Day + "](" + todaysPosts.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + Environment.NewLine + ">" + Environment.NewLine;
                    newSidebarContent += Environment.NewLine + "* [There are " + todaysPosts.First().ListComments(2000).Count + " events today. Check it out or add your own.](" + todaysPosts.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + Environment.NewLine + "&nbsp;" + Environment.NewLine;

                    newSidebarContent += "######" + today.ToString("MMMM");
                    newSidebarContent += Environment.NewLine + "| Su | Mo | Tu | We | Th | Fr | Sa |" + Environment.NewLine + "|-|-|-|-|-|-|-|";

                    for (var i = 1; i <= DateTime.DaysInMonth(today.Year, today.Month); i++)
                    {
                        var date = new DateTime(today.Year, today.Month, i);

                        if (i == 1)
                            newSidebarContent += Environment.NewLine + string.Join("|", Enumerable.Range(0, (int)date.DayOfWeek + 1).Select(d => string.Empty));

                        var dailyPost = dailyPosts.Where(p => p.AuthorName == user.Name && p.Title == string.Format(DailyPostTitleFormat, today.Year, today.ToString("MMMM"), i)).ToList();

                        if (dailyPost.Any())
                            newSidebarContent += "| [" + i + "](" + dailyPost.First().Shortlink + ")";
                        else
                            newSidebarContent += "| " + i;

                        if (date.DayOfWeek == DayOfWeek.Saturday)
                            newSidebarContent += "|" + Environment.NewLine;
                    }

                    newSidebarContent += Environment.NewLine + Environment.NewLine + "&nbsp;" + Environment.NewLine;
                    newSidebarContent += Environment.NewLine + SidebarSectionAdditional;

                    settings.Sidebar = sidebar.Remove(startIndex, endIndex - startIndex).Insert(startIndex, Environment.NewLine + Environment.NewLine + newSidebarContent + Environment.NewLine + Environment.NewLine);
                    settings.UpdateSettings();
                }
                else
                {
                    reddit.ComposePrivateMessage("No sidebar section found", "There was no sidebar section found for updating.", SubredditName);
                }
            }
            else
            {
                reddit.ComposePrivateMessage("No daily post found", "There was no daily comment found for " + today.ToString("MMMM d, yyyy") + ".", SubredditName);
            }

            // Ensure next months post exists if we're more than 25 days into the current month
            if (today.Day > 25)
            {
                var nextMonth = today.AddMonths(1);
                GetOrCreateDailyPosts(reddit, subreddit, user, nextMonth.Year, nextMonth.Month).ToList();
            }
        }
    }
}
