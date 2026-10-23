using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void SupportContactsPage(object data)
        {
            Sub("Contact support");
            foreach (object contact in J.List(J.Get(data, "contacts")))
            {
                string type = J.S(contact, "type"), content = J.S(contact, "content"), title = J.S(contact, "title").NullIfEmpty() ?? "Support";
                var panel = new StackPanel(); panel.Children.Add(Text(title, 19, "#17263C", true));
                if ((type == "CHAT" || type == "HELP_LINK") && ServiceLinks.ProviderNavigation(content))
                {
                    panel.Children.Add(Text(new Uri(content).Host, 13, "#64738A"));
                    panel.Children.Add(LocalButton(type == "CHAT" ? "Open support chat" : "Open support page", delegate
                    { new ServiceBrowserWindow(title, content, null, null, false, null, null, true) { Owner = this }.Show(); }));
                }
                else
                {
                    panel.Children.Add(Text(content, 14));
                    if (content.Length > 0) panel.Children.Add(LocalButton("Copy contact details", delegate { Clipboard.SetText(content); SetNotice("Support contact copied."); }));
                }
                page.Children.Add(Card(panel));
            }
            if (J.List(J.Get(data, "contacts")).Count == 0) page.Children.Add(Text("No account-specific support contacts were returned. Open official support for the available contact options.", 14, "#64738A"));
            page.Children.Add(LocalButton("Official eero support", delegate { OpenServiceWindow("eero support", "https://www.eero.com/support"); }));
        }
    }
}
