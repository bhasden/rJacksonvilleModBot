using HtmlAgilityPack;
using Microsoft.Azure;
using RedditSharp;
using RedditSharp.Things;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace rJacksonvilleModBot
{
    class Program
    {
        private const string DailyEventsUrl = "http://events.jacksonville.com/calendar/day/{0}-{1}-{2}";
        private const string DailyPostDescription = "Know of an event on {0}? Post it here and when the date comes around, it'll be linked to from the sidebar.";
        private const string DailyPostTitleFormat = "Jacksonville Events Calendar: {1} {2}, {0}";
        private const string SidebarSectionMarkdown = "#**Events and Entertainment**";
        private const string SidebarSectionAdditional = "\r\n**More event & entertainment resources**\r\n\r\n* [Downtown Jacksonville](http://downtownjacksonville.org/Downtown_Vision_Inc_Home.aspx)\r\n* [Jax.com Events](http://events.jacksonville.com/)\r\n* [JaxEvents](http://www.jaxevents.com/)\r\n* [Folio Events](http://folioweekly.com/calendar)\r\n* [Jax4Kids - Family Friendly Events](http://jax4kids.com/)";
        private const string SubredditName = "/r/Jacksonville";

        private static void CreateDailyEvents(Post post, int year, int month, int day)
        {
            var web = new HtmlWeb();
            var url = string.Format(DailyEventsUrl, year, month, day);
            var doc = web.Load(url);

            foreach (var eventNode in doc.DocumentNode.SelectNodes("//div[@class='listing-item']").Reverse())
            {
                var eventComment = string.Empty;

                var howNode = eventNode.SelectSingleNode("p[@class='item_how']/span/a");
                var whatNode = eventNode.SelectSingleNode("span/p[@class='item_what']/a");
                var whenNode = eventNode.SelectSingleNode("p[@class='item_when']/time");
                var whereNode = eventNode.SelectSingleNode("span/p[@class='item_where']/a");

                if (whatNode != null)
                    eventComment += "Event: [" + whatNode.InnerText + "]" + "(" + new Uri(new Uri(url), whatNode.Attributes["href"].Value) + ")" + Environment.NewLine + Environment.NewLine;

                if (whereNode != null)
                    eventComment += "Place: [" + whereNode.InnerText + "]" + "(" + new Uri(new Uri(url), whereNode.Attributes["href"].Value) + ")" + Environment.NewLine + Environment.NewLine;

                if (whenNode != null)
                    eventComment += "Time: " + whenNode.InnerText + Environment.NewLine + Environment.NewLine;

                if (howNode != null)
                    eventComment += "[" + howNode.InnerText + "]" + "(" + new Uri(new Uri(url), howNode.Attributes["href"].Value) + ")" + Environment.NewLine + Environment.NewLine;

                if (!string.IsNullOrWhiteSpace(eventComment))
                {
                    eventComment += "&nbsp;" + Environment.NewLine + Environment.NewLine + "^^source: ^^[Jacksonville&nbsp;Events&nbsp;Calendar](http://events.jacksonville.com/)";

                    post.Comment(eventComment);
                }

                Thread.Sleep(3000); // Wait 3 seconds before posting another comment
            }
        }

        private static IEnumerable<Post> GetOrCreateDailyPosts(Reddit reddit, Subreddit subreddit, AuthenticatedUser user, int year, int month)
        {
            var firstDateOfMonth = new DateTime(year, month, 1);
            var monthName = firstDateOfMonth.ToString("MMMM");

            var createdPost = false;
            var dailyPostDate = firstDateOfMonth;
            var userPosts = user.Posts.ToList();

            while (dailyPostDate.Month == month)
            {
                var dailyPostTitle = string.Format(DailyPostTitleFormat, year, monthName, dailyPostDate.Day);
                var dailyPosts = userPosts.Where(p => p.Title == dailyPostTitle).OrderByDescending(p => p.Created);

                if (dailyPosts.Any())
                {
                    // Drop any existing posts. This can occur during a partial run or error event.
                    foreach (var dailyPost in dailyPosts)
                        dailyPost.Del();
                }

                // Create the post for the day.
                var post = subreddit.SubmitTextPost(dailyPostTitle, string.Format(DailyPostDescription, dailyPostDate.ToLongDateString()));

                CreateDailyEvents(post, dailyPostDate.Year, dailyPostDate.Month, dailyPostDate.Day);

                yield return post;

                createdPost = true;
                Thread.Sleep(15000); // Wait 15 seconds before creating another post

                dailyPostDate = dailyPostDate.AddDays(1);
            }

            if (createdPost)
                reddit.ComposePrivateMessage("New month worth of posts created", "Daily posts were created for " + monthName + ".", SubredditName);
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
            foreach (var message in user.PrivateMessages.Where(m => m.Unread))
            {
                message.Reply("You have messaged the " + SubredditName + " moderator bot. These private messages are not actively monitored.");
                message.SetAsRead();
            }

            // Get or create the post for this month
            var today = DateTime.Now;
            var dailyPosts = GetOrCreateDailyPosts(reddit, subreddit, user, today.Year, today.Month).ToList();
            var todaysPosts = dailyPosts.Where(p => p.AuthorName == user.Name && p.Title == string.Format(DailyPostTitleFormat, today.Year, today.ToString("MMMM"), today.Day)).ToList();

            if (dailyPosts.Any())
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
