using HtmlAgilityPack;
using Microsoft.Azure;
using RedditSharp;
using RedditSharp.Things;
using System;
using System.Linq;
using System.Threading;

namespace rJacksonvilleModBot
{
    class Program
    {
        private const string DailyCommentFormat = "Reply here with events on {0}.";
        private const string DailyEventsUrl = "http://events.jacksonville.com/calendar/day/{0}-{1}-{2}";
        private const string MonthlyPostTitleFormat = "Post {0} {1} Events Here";//"Jacksonville Events Calendar: Post {0} {1} Events Here";
        private const string SidebarSectionMarkdown = "#**Events and Entertainment**";
        private const string SidebarSectionAdditional = "\r\n**More event & entertainment resources**\r\n\r\n* [Downtown Jacksonville](http://downtownjacksonville.org/Downtown_Vision_Inc_Home.aspx)\r\n* [Jax.com Events](http://events.jacksonville.com/)\r\n* [JaxEvents](http://www.jaxevents.com/)\r\n* [Folio Events](http://folioweekly.com/calendar)\r\n* [Jax4Kids - Family Friendly Events](http://jax4kids.com/)";
        private const string SubredditName = "/r/Jacksonville";

        private static void CreateDailyEvents(Comment comment, int year, int month, int day)
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

                    comment.Reply(eventComment);
                }

                Thread.Sleep(3000); // Wait 3 seconds before posting another comment
            }
        }

        private static Post GetOrCreateMonthlyPost(Reddit reddit, Subreddit subreddit, AuthenticatedUser user, int year, int month)
        {
            var firstDateOfMonth = new DateTime(year, month, 1);
            var monthName = firstDateOfMonth.ToString("MMMM");
            var monthPostTitle = string.Format(MonthlyPostTitleFormat, monthName, year);
            var monthPosts = user.Posts.Where(p => p.Author.Name == user.Name && p.Title == monthPostTitle).OrderByDescending(p => p.Created);

            if (monthPosts.Any())
            {
                if (monthPosts.Count() > 1)
                    reddit.ComposePrivateMessage("Multiple monthly posts found", "Multiple posts found for " + monthName + " monthly post." + Environment.NewLine + string.Join(Environment.NewLine, monthPosts.Select(p => p.Shortlink)), SubredditName);

                return monthPosts.First();
            }

            // Create the post for the month if it doesn't exist.
            var post = subreddit.SubmitTextPost(monthPostTitle, "Know of something going on in " + monthName + "? Post it here under the appropriately dated comment. When the date comes around, it'll be linked to from the sidebar.");
            var dailyCommentDate = firstDateOfMonth;

            while (dailyCommentDate.Month == month)
            {
                var dailyComment = post.Comment(string.Format(DailyCommentFormat, dailyCommentDate.ToString("MMMM d, yyyy")));

                CreateDailyEvents(dailyComment, dailyCommentDate.Year, dailyCommentDate.Month, dailyCommentDate.Day);

                dailyCommentDate = dailyCommentDate.AddDays(1);

                Thread.Sleep(10000); // Wait 10 seconds before posting another comment
            }

            reddit.ComposePrivateMessage("New monthly post created", "Monthly events post created for " + monthName + " at " + post.Shortlink + ".", SubredditName);

            return post;
        }

        static void Main(string[] args)
        {
            var password = string.Empty;
            var username = string.Empty;
            
            if (args != null && args.Count() == 2)
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
            var thisMonthPost = GetOrCreateMonthlyPost(reddit, subreddit, user, today.Year, today.Month);
            var todaysComments = thisMonthPost.ListComments(2000).Where(c => c.Author == user.Name && c.Body == string.Format(DailyCommentFormat, today.ToString("MMMM d, yyyy"))).ToList();

            if (todaysComments.Any())
            {
                if (todaysComments.Count() > 1)
                    reddit.ComposePrivateMessage("Multiple daily posts found", "Multiple posts found for " + today.ToString("MMMM d, yyyy") + " daily post." + Environment.NewLine + string.Join(Environment.NewLine, todaysComments.Select(c => c.Shortlink)), SubredditName);

                var settings = subreddit.Settings;
                var sidebar = settings.Sidebar;

                if (!string.IsNullOrEmpty(sidebar) && sidebar.Contains(SidebarSectionMarkdown))
                {
                    var startIndex = sidebar.IndexOf(SidebarSectionMarkdown, StringComparison.Ordinal) + SidebarSectionMarkdown.Length;
                    var endIndex = sidebar.IndexOf("#**", startIndex, StringComparison.Ordinal); // Find the beginning of the next section 

                    if (endIndex < 0)
                        endIndex = sidebar.Length - 1; // There's no next section, so just replace the rest of the content

                    var newSidebarContent = "* [" + today.DayOfWeek + "](" + todaysComments.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + "* [" + today.ToString("MMMM") + "](" + todaysComments.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + "* [" + today.Day + "](" + todaysComments.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + Environment.NewLine + "&nbsp;" + Environment.NewLine;
                    newSidebarContent += Environment.NewLine + "* [There are " + todaysComments.First().Comments.Count() + " events today. Check it out or add your own.](" + todaysComments.First().Shortlink + ")";
                    newSidebarContent += Environment.NewLine + "* [Additionally, there are " + thisMonthPost.ListComments(2000).Where(c => c.Author == user.Name).Sum(c => c.Comments.Count) + " events posted for this month.](" + thisMonthPost.Shortlink + ")";
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
                var nextMonthPost = GetOrCreateMonthlyPost(reddit, subreddit, user, nextMonth.Year, nextMonth.Month);
            }

            // TODO: Find or add the API methods for automatically making certain Monthly posts sticky.
        }
    }
}
