using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace EeroDesktop
{
    public static class BillingContracts
    {
        public static bool Return(Uri uri)
        {
            if (uri.UserInfo.Length > 0) return false;
            string[] pages = { "pluspurchasecomplete", "businesspurchasecomplete", "datapackpurchasecomplete" };
            if (uri.Scheme == "eero" && uri.Host == "jumpto") return pages.Contains(uri.AbsolutePath.Trim('/'));
            if (uri.Scheme == "https" && uri.Host == "app.eero.com" && uri.Port == 443) return pages.Any(p => uri.AbsolutePath == "/" + p || uri.AbsolutePath == "/" + p + "/index.html" || uri.AbsolutePath == "/jumpto/" + p);
            return false;
        }
    }
    public sealed partial class MainWindow
    {
        private void OpenBillingWindow(string url, string header = null)
        {
            if (!ServiceLinks.Allowed(url)) throw new InvalidOperationException("The billing service returned an unsupported provider address.");
            int epoch = notificationEpoch;
            var window = new ServiceBrowserWindow("eero billing", url, header, null, false, BillingContracts.Return, async uri => { await Task.FromResult(0); }, true) { Owner = this };
            window.Closed += async delegate
            {
                if (api.SignedIn && notificationEpoch == epoch) await Run(RefreshBilling, "Checking subscription status…");
            };
            window.Show();
        }
        private async Task RefreshBilling()
        {
            if (preview) throw new InvalidOperationException("Sign in to refresh subscription benefits.");
            int epoch = notificationEpoch;
            object customer = await api.Send("GET", "/2.2/premium/customer");
            if (!api.SignedIn || notificationEpoch != epoch) return;
            featureData["billing_customer"] = customer;
            if (!String.IsNullOrEmpty(networkUrl)) await LoadNetwork();
            if (api.SignedIn && notificationEpoch == epoch) { RenderPage(); SetNotice("Subscription status refreshed from eero. Purchase and cancellation status comes from the service, including changes made in the billing window."); }
        }
        private void BillingOverview()
        {
            Sub("Billing & subscription status"); object customer; featureData.TryGetValue("billing_customer", out customer);
            if (preview) customer = J.Obj("plan", "eero Plus", "status", "Active", "payment_channel", "eero", "renewal", "See billing service for your next renewal date and price");
            ObjectCards(customer, "Current subscription");
            ActionRow(Button("Refresh benefits / restore access", RefreshBilling), Button("Manage subscription & payment method", async delegate { await OpenPaymentService("MANAGE_SUBSCRIPTION"); }), Button("Purchase history", async delegate { await OpenPaymentService("MANAGE_PURCHASES"); }));
            page.Children.Add(Section("Plans, offers & referrals", "Settings/Account settings/Subscriptions/Offers & referrals"));
            page.Children.Add(Text("A billing-window return does not prove that a purchase succeeded. Refresh checks your current entitlement with eero. Purchases made through Google Play or another provider remain managed by that billing provider.", 13, "#64738A"));
        }
        private void OrganizationBilling()
        {
            Sub("Business billing");
            page.Children.Add(Text("Manage the payment account for your eero business organization. Access requires the appropriate organization role.", 15));
            ActionRow(Button("Manage business payment account", async delegate
            {
                object result = await Read("/2.2/organizations/self/payment_account/management_url");
                OpenBillingWindow(J.S(result, "external_payment_account_management_url"));
            }), Button("Set up business payment account", async delegate
            {
                if (preview) throw new InvalidOperationException("Sample mode does not create payment accounts.");
                if (!Confirm("Start payment-account onboarding for your eero business organization? Review the provider's requirements before submitting its form.")) return;
                object result = await api.Send("POST", "/2.2/organizations/self/payment_account");
                OpenBillingWindow(J.S(result, "external_payment_account_onboard_url"));
            }));
        }
    }
}
