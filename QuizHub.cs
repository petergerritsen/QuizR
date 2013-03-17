using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNet.SignalR;

namespace QuizR {
    public class QuizGroup {
        public string Name { get; set; }
        public string AdminConnectionId { get; set; }
        public HashSet<string> ConnectionIds { get; set; }
        public List<string> Teamnames { get; set; }
        public string Winner { get; set; }
    }

    public class User {
        public string ConnectionId { get; set; }
        public string Name { get; set; }
        public string Teamname { get; set; }
        public string Groupname { get; set; }
    }

    public class QuizHub : Hub {
        private static readonly ConcurrentDictionary<string, QuizGroup> QuizGroups = new ConcurrentDictionary<string, QuizGroup>();
        private static readonly ConcurrentDictionary<string, User> Users = new ConcurrentDictionary<string, User>();

        public override Task OnConnected() {
            if (!Users.ContainsKey(Context.ConnectionId))
                return null;

            var user = Users[Context.ConnectionId];
            var group = QuizGroups[user.Groupname];

            return Clients.Client(group.AdminConnectionId).connected(string.Format("{0} - {1}", user.Teamname, user.Name));
        }

        public override Task OnDisconnected() {
            if (!Users.ContainsKey(Context.ConnectionId))
                return null;

            var user = Users[Context.ConnectionId];
            var group = QuizGroups[user.Groupname];

            return Clients.Client(group.AdminConnectionId).leave(string.Format("{0} - {1}", user.Teamname, user.Name));
        }

        public override Task OnReconnected() {
            if (!Users.ContainsKey(Context.ConnectionId))
                return null;

            var user = Users[Context.ConnectionId];
            var group = QuizGroups[user.Groupname];

            return Clients.Client(group.AdminConnectionId).rejoined(string.Format("{0} - {1}", user.Teamname, user.Name));
        }

        public bool Create(string groupName, string teamNames) {
            // Check if group with name exists
            if (QuizGroups.ContainsKey(groupName)) {
                return false;
            }

            var group = QuizGroups.GetOrAdd(groupName, _ => new QuizGroup {
                Name = groupName,
                AdminConnectionId = Context.ConnectionId,
                ConnectionIds = new HashSet<string>(),
                Teamnames = teamNames.Split('#').ToList()
            });

            group.ConnectionIds.Add(Context.ConnectionId);
            Groups.Add(Context.ConnectionId, group.Name);

            return true;
        }
        
        public bool Join(string groupName, string teamname, string username) {
            if (!QuizGroups.ContainsKey(groupName))
                return false;
            
            string connectionId = Context.ConnectionId;

            var group = QuizGroups.GetOrAdd(groupName, _ => new QuizGroup {
                Name = groupName,
                ConnectionIds = new HashSet<string>()
            });

            if (!group.Teamnames.Contains(teamname)) {
                return false;
            }

            Groups.Add(Context.ConnectionId, group.Name);

            lock (group.ConnectionIds) {
                group.ConnectionIds.Add(connectionId);
            }

            // Broadcast the connected teamname and username to admin 
            Clients.Group(group.Name).joined(string.Format("{0} - {1}", teamname, username));

            var user = Users.GetOrAdd(connectionId, _ => new User {
                ConnectionId = connectionId,
                Name = username,
                Teamname = teamname,
                Groupname = groupName
            });

            return true;
        }
        
        public void Unblock(string groupName) {
            var group = QuizGroups[groupName];
            if (group.AdminConnectionId != Context.ConnectionId)
                return;

            lock (group) {
                group.Winner = null;
            }

            Clients.Group(groupName).unblock();
        }

        public bool PushButton() {
            var connectionId = Context.ConnectionId;
            var user = Users[connectionId];
            if (user == null)
                return false;

            var group = QuizGroups[user.Groupname];
            lock (group) {
                if (string.IsNullOrEmpty(group.Winner)) {
                    string winner = string.Format("{0} - {1}", user.Teamname, user.Name);
                    group.Winner = winner;

                    Clients.Group(group.Name).block(winner, user.Teamname);
                    
                    return true;
                }
            }

            return false;
        }
    }
}