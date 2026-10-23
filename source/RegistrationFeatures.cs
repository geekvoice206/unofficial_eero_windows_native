using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private string registrationCountry;
        private Dictionary<string, object> registrationConsents = new Dictionary<string, object>();
        private void BeginRegistrationUi()
        {
            Edit("Create an eero account", new[] { new Field("country", "Two-letter country code", "US") }, async values =>
            {
                string country = V(values, "country").Trim().ToUpperInvariant();
                if (!Regex.IsMatch(country, "^[A-Z]{2}$")) throw new ArgumentException("Enter a two-letter country code, such as US or CA.");
                object consents = await api.RegistrationConsents(country);
                QueueRegistration(country, J.List(consents));
            }, "Continue", "Use this only if you do not already have an eero account. Phone/SMS verification is required.");
        }
        private void QueueRegistration(string country, List<object> consents) { Dispatcher.BeginInvoke(new Action(delegate { RegistrationForm(country, consents); })); }
        private void RegistrationForm(string country, List<object> agreements)
        {
            var fields = new List<Field> { new Field("name", "Your name"), new Field("email", "Email address"), new Field("phone", "Phone number with country code"), new Field("remember", "Remember this Windows account", true, "bool") };
            fields.Add(new Field("legal_link", "Review eero terms & privacy", "https://www.eero.com/legal", "link"));
            foreach (object agreement in agreements)
            {
                string key = J.S(agreement, "name"); if (key.Length == 0) continue;
                fields.Add(new Field("consent:" + key, J.S(agreement, "localized_consent").NullIfEmpty() ?? Human(key), false, "bool"));
            }
            if (agreements.Count == 0) fields.Add(new Field("legal", "I have reviewed and agree to eero's terms and privacy policy", false, "bool"));
            Edit("Account details and consent", fields, async values =>
            {
                ValidateName(V(values, "name")); string email = V(values, "email").Trim();
                if (!Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) throw new ArgumentException("Enter a valid email address.");
                var choices = new Dictionary<string, object>();
                foreach (object agreement in agreements)
                {
                    string key = J.S(agreement, "name"); if (key.Length == 0) continue;
                    bool consent = Flag(values, "consent:" + key);
                    if (J.B(agreement, "requires_consent") && !consent) throw new ArgumentException("A required consent has not been accepted. Review the agreement before continuing.");
                    choices[key] = consent;
                }
                if (agreements.Count == 0 && !Flag(values, "legal")) throw new ArgumentException("Review and accept eero's terms and privacy policy before creating an account.");
                if (!Confirm("Create a new eero account with these details and request a verification code?")) return;
                api.Remember = Flag(values, "remember");
                await api.BeginRegistration(V(values, "name").Trim(), email, PhoneInput.Normalize(V(values, "phone")));
                registrationCountry = country; registrationConsents = choices; resendAfter = DateTime.UtcNow.AddSeconds(60);
                ShowLogin(true); SetNotice("Account creation started. Enter the verification code sent to your phone.");
            }, "Create account", "The displayed consent text comes from eero. Required consents must be explicitly accepted; optional consents start unchecked. Review eero's terms at https://www.eero.com/legal before continuing.");
        }
    }
}
