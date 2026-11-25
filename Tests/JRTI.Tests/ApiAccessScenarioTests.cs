using System;
using System.Collections.Generic;
using JustReadTheInstructions;
using Xunit;
using static JustReadTheInstructions.ApiAccess;

namespace JRTI.Tests
{
    public class ApiAccessScenarioTests
    {
        private const string Key = "ABCDEFGHJ2";

        private sealed class Request
        {
            public bool ConnectionIsLocal;
            public string Host;
            public string KeyQuery;
            public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public Request With(string name, string value)
            {
                Headers[name] = value;
                return this;
            }

            public Request WithKeyQuery(string key)
            {
                KeyQuery = key;
                return this;
            }

            public WriteVerdict Check(bool requireKey)
            {
                Assert.True(IsAllowedHost(Host, ""), $"{Host} should be an allowed name");
                return CheckWrite(requireKey, Key, ConnectionIsLocal, Host, KeyQuery, name => Headers.TryGetValue(name, out var v) ? v : null);
            }
        }

        private static Request BrowserPage(bool connectionIsLocal, string host)
            => new Request { ConnectionIsLocal = connectionIsLocal, Host = host }
                .With("Origin", $"http://{host}").With("Sec-Fetch-Site", "same-origin");

        private static Request KspComputerAtLocalhost() => BrowserPage(true, "localhost:8080");
        private static Request KspComputerAtItsNetworkAddress() => BrowserPage(true, "192.168.1.10:8080");
        private static Request PhoneOnWifi() => BrowserPage(false, "192.168.1.10:8080");
        private static Request PhoneOnTailscaleIp() => BrowserPage(false, "100.68.55.4:8080");

        private static Request PhoneThroughTailscaleServe()
            => BrowserPage(true, "gamepc.tail1234.ts.net").With("X-Forwarded-For", "100.101.102.103");

        private static Request StreamDeckOnAnotherComputer() => new Request { ConnectionIsLocal = false, Host = "192.168.1.10:8080" };

        private static Request OtherWebsiteInThePhonesBrowser()
            => new Request { ConnectionIsLocal = false, Host = "192.168.1.10:8080" }
                .With("Origin", "https://evil.example").With("Sec-Fetch-Site", "cross-site");

        private static Request ViteDevServerOnTheKspComputer()
            => new Request { ConnectionIsLocal = true, Host = "localhost:8080" }
                .With("Origin", "http://localhost:5173").With("Sec-Fetch-Site", "same-site");

        [Fact]
        public void WithTheKeyOffEveryDeviceMayChangeThings()
        {
            Assert.Equal(WriteVerdict.Allowed, PhoneOnWifi().Check(requireKey: false));
            Assert.Equal(WriteVerdict.Allowed, PhoneOnTailscaleIp().Check(requireKey: false));
            Assert.Equal(WriteVerdict.Allowed, PhoneThroughTailscaleServe().Check(requireKey: false));
            Assert.Equal(WriteVerdict.Allowed, StreamDeckOnAnotherComputer().Check(requireKey: false));
        }

        [Fact]
        public void OtherWebsitesAreBlockedWithOrWithoutTheKey()
        {
            Assert.Equal(WriteVerdict.OtherWebsite, OtherWebsiteInThePhonesBrowser().Check(requireKey: false));
            Assert.Equal(WriteVerdict.OtherWebsite, OtherWebsiteInThePhonesBrowser().Check(requireKey: true));
            Assert.Equal(WriteVerdict.OtherWebsite, OtherWebsiteInThePhonesBrowser().With("Authorization", $"Bearer {Key}").Check(requireKey: true));
        }

        [Fact]
        public void TheKspComputerAtLocalhostNeverNeedsTheKey()
        {
            Assert.Equal(WriteVerdict.Allowed, KspComputerAtLocalhost().Check(requireKey: true));
            Assert.Equal(WriteVerdict.Allowed, ViteDevServerOnTheKspComputer().Check(requireKey: true));
        }

        [Fact]
        public void TheKspComputerAtItsNetworkAddressNeedsTheKey()
        {
            Assert.Equal(WriteVerdict.NeedsKey, KspComputerAtItsNetworkAddress().Check(requireKey: true));
            Assert.Equal(WriteVerdict.Allowed, KspComputerAtItsNetworkAddress().With("Authorization", $"Bearer {Key}").Check(requireKey: true));
        }

        [Fact]
        public void PhonesNeedTheKey()
        {
            Assert.Equal(WriteVerdict.NeedsKey, PhoneOnWifi().Check(requireKey: true));
            Assert.Equal(WriteVerdict.NeedsKey, PhoneOnTailscaleIp().Check(requireKey: true));
            Assert.Equal(WriteVerdict.NeedsKey, PhoneOnWifi().With("Authorization", "Bearer ABCDEFGHJ3").Check(requireKey: true));
            Assert.Equal(WriteVerdict.Allowed, PhoneOnWifi().With("Authorization", $"Bearer {Key}").Check(requireKey: true));
            Assert.Equal(WriteVerdict.Allowed, PhoneOnTailscaleIp().With("Authorization", $"Bearer {Key.ToLowerInvariant()}").Check(requireKey: true));
        }

        [Fact]
        public void TailscaleServeDoesNotCountAsTheKspComputer()
        {
            Assert.Equal(WriteVerdict.NeedsKey, PhoneThroughTailscaleServe().Check(requireKey: true));
            Assert.Equal(WriteVerdict.Allowed, PhoneThroughTailscaleServe().With("Authorization", $"Bearer {Key}").Check(requireKey: true));
        }

        [Fact]
        public void StreamDeckLinksCarryTheKeyInTheAddress()
        {
            Assert.Equal(WriteVerdict.NeedsKey, StreamDeckOnAnotherComputer().Check(requireKey: true));
            Assert.Equal(WriteVerdict.Allowed, StreamDeckOnAnotherComputer().WithKeyQuery(Key).Check(requireKey: true));
        }
    }
}
