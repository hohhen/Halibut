using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
#if !NET40
using System.Net.Http;
#endif
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentAssertions;
using Halibut.ServiceModel;
using Halibut.Tests.TestServices;
using Newtonsoft.Json.Bson;
using Xunit;

namespace Halibut.Tests
{
    public class UsageFixture
    {
        DelegateServiceFactory services;

        public UsageFixture()
        {
            services = new DelegateServiceFactory();
            services.Register<IEchoService>(() => new EchoService());
        }

        [Fact]
        public void OctopusCanDiscoverTentacle()
        {
            using (var octopus = new HalibutRuntime(services, Certificates.Octopus))
            using (var tentacleListening = new HalibutRuntime(services, Certificates.TentacleListening))
            {
                var tentaclePort = tentacleListening.Listen();

                var info = octopus.Discover(new Uri("https://localhost:" + tentaclePort));
                info.RemoteThumbprint.Should().Be(Certificates.TentacleListeningPublicThumbprint);
            }
        }

        [Fact]
        public void OctopusCanSendMessagesToListeningTentacle()
        {
            using (var octopus = new HalibutRuntime(services, Certificates.Octopus))
            using (var tentacleListening = new HalibutRuntime(services, Certificates.TentacleListening))
            {
                var tentaclePort = tentacleListening.Listen();
                tentacleListening.Trust(Certificates.OctopusPublicThumbprint);

                var echo = octopus.CreateClient<IEchoService>("https://localhost:" + tentaclePort, Certificates.TentacleListeningPublicThumbprint);
                echo.SayHello("Deploy package A").Should().Be("Deploy package A...");
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < 2000; i++)
                {
                    echo.SayHello("Deploy package A").Should().Be("Deploy package A...");
                }

                Console.WriteLine("Complete in {0:n0}ms", watch.ElapsedMilliseconds);
            }
        }

        [Fact]
        public void OctopusCanSendMessagesToPollingTentacle()
        {
            using (var octopus = new HalibutRuntime(services, Certificates.Octopus))
            using (var tentaclePolling = new HalibutRuntime(services, Certificates.TentaclePolling))
            {
                var octopusPort = octopus.Listen();
                octopus.Trust(Certificates.TentaclePollingPublicThumbprint);

                tentaclePolling.Poll(new Uri("poll://SQ-TENTAPOLL"), new ServiceEndPoint(new Uri("https://localhost:" + octopusPort), Certificates.OctopusPublicThumbprint));

                var echo = octopus.CreateClient<IEchoService>("poll://SQ-TENTAPOLL", Certificates.TentaclePollingPublicThumbprint);
                for (var i = 0; i < 2000; i++)
                {
                    echo.SayHello("Deploy package A" + i).Should().Be("Deploy package A" + i + "...");
                }
            }
        }

#if HAS_SERVICE_POINT_MANAGER
        [Fact]
        public void OctopusCanSendMessagesToWebSocketPollingTentacle()
        {
            const int octopusPort = 8450;
            AddSslCertToLocalStoreAndRegisterFor("0.0.0.0:" + octopusPort);

            try
            {
                using (var octopus = new HalibutRuntime(services, Certificates.Octopus))
                using (var tentaclePolling = new HalibutRuntime(services, Certificates.TentaclePolling))
                {
                    octopus.ListenWebSocket($"https://+:{octopusPort}/Halibut");
                    octopus.Trust(Certificates.TentaclePollingPublicThumbprint);

                    tentaclePolling.Poll(new Uri("poll://SQ-TENTAPOLL"), new ServiceEndPoint(new Uri($"wss://localhost:{octopusPort}/Halibut"), Certificates.SslThumbprint));

                    var echo = octopus.CreateClient<IEchoService>("poll://SQ-TENTAPOLL", Certificates.TentaclePollingPublicThumbprint);
                    for (var i = 0; i < 2000; i++)
                    {
                        echo.SayHello("Deploy package A" + i).Should().Be("Deploy package A" + i + "...");
                    }
                }
            }
            finally
            {
                RemoveSslCertBindingFor("0.0.0.0:" + octopusPort);
            }
        }
#endif

        [Fact]
        public void StreamsCanBeSentToListening()
        {
            using (var octopus = new HalibutRuntime(services, Certificates.Octopus))
            using (var tentacleListening = new HalibutRuntime(services, Certificates.TentacleListening))
            {
                var tentaclePort = tentacleListening.Listen();
                tentacleListening.Trust(Certificates.OctopusPublicThumbprint);

                var data = new byte[1024 * 1024 + 15];
                new Random().NextBytes(data);

                var echo = octopus.CreateClient<IEchoService>("https://localhost:" + tentaclePort, Certificates.TentacleListeningPublicThumbprint);

                for (var i = 0; i < 100; i++)
                {
                    var count = echo.CountBytes(DataStream.FromBytes(data));
                    count.Should().Be(1024 * 1024 + 15);
                }
            }
        }

        [Fact]
        public void StreamsCanBeSentToPolling()
        {
            using (var octopus = new HalibutRuntime(services, Certificates.Octopus))
            using (var tentaclePolling = new HalibutRuntime(services, Certificates.TentaclePolling))
            {
                var octopusPort = octopus.Listen();
                octopus.Trust(Certificates.TentaclePollingPublicThumbprint);

                tentaclePolling.Poll(new Uri("poll://SQ-TENTAPOLL"), new ServiceEndPoint(new Uri("https://localhost:" + octopusPort), Certificates.OctopusPublicThumbprint));

                var echo = octopus.CreateClient<IEchoService>("poll://SQ-TENTAPOLL", Certificates.TentaclePollingPublicThumbprint);

                var data = new byte[1024 * 1024 + 15];
                new Random().NextBytes(data);

                for (var i = 0; i < 100; i++)
                {
                    var count = echo.CountBytes(DataStream.FromBytes(data));
                    count.Should().Be(1024 * 1024 + 15);
                }
            }
        }

        [Fact]
        public void SupportsDifferentServiceContractMethods()
        {
            services.Register<ISupportedServices>(() => new SupportedServices());
            using (var octopus = new HalibutRuntime(Certificates.Octopus))
            using (var tentacleListening = new HalibutRuntime(services, Certificates.TentacleListening))
            {
                tentacleListening.Trust(Certificates.OctopusPublicThumbprint);
                var tentaclePort = tentacleListening.Listen();

                var echo = octopus.CreateClient<ISupportedServices>("https://localhost:" + tentaclePort, Certificates.TentacleListeningPublicThumbprint);
                echo.MethodReturningVoid(12, 14);

                echo.Hello().Should().Be("Hello");
                echo.Hello("a").Should().Be("Hello a");
                echo.Hello("a", "b").Should().Be("Hello a b");
                echo.Hello("a", "b", "c").Should().Be("Hello a b c");
                echo.Hello("a", "b", "c", "d").Should().Be("Hello a b c d");
                echo.Hello("a", "b", "c", "d", "e").Should().Be("Hello a b c d e");
                echo.Hello("a", "b", "c", "d", "e", "f").Should().Be("Hello a b c d e f");
                echo.Hello("a", "b", "c", "d", "e", "f", "g").Should().Be("Hello a b c d e f g");
                echo.Hello("a", "b", "c", "d", "e", "f", "g", "h").Should().Be("Hello a b c d e f g h");
                echo.Hello("a", "b", "c", "d", "e", "f", "g", "h", "i").Should().Be("Hello a b c d e f g h i");
                echo.Hello("a", "b", "c", "d", "e", "f", "g", "h", "i", "j").Should().Be("Hello a b c d e f g h i j");
                echo.Hello("a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k").Should().Be("Hello a b c d e f g h i j k");

                echo.Add(1, 2).Should().Be(3);
                echo.Add(1.00, 2.00).Should().Be(3.00);
                echo.Add(1.10M, 2.10M).Should().Be(3.20M);

                echo.Ambiguous("a", "b").Should().Be("Hello string");
                echo.Ambiguous("a", new Tuple<string, string>("a", "b")).Should().Be("Hello tuple");

                var ex = Assert.Throws<HalibutClientException>(() => echo.Ambiguous("a", (string)null));
                ex.Message.Should().Contain("Ambiguous");
            }
        }

        [Fact]
        public void StreamsCanBeSentToListeningWithProgressReporting()
        {
            using (var octopus = new HalibutRuntime(services, Certificates.Octopus))
            using (var tentacleListening = new HalibutRuntime(services, Certificates.TentacleListening))
            {
                var tentaclePort = tentacleListening.Listen();
                tentacleListening.Trust(Certificates.OctopusPublicThumbprint);

                var progressReported = new List<int>();

                var data = new byte[1024 * 1024 * 16 + 15];
                new Random().NextBytes(data);
                var stream = new MemoryStream(data);

                var echo = octopus.CreateClient<IEchoService>("https://localhost:" + tentaclePort, Certificates.TentacleListeningPublicThumbprint);

                var count = echo.CountBytes(DataStream.FromStream(stream, progressReported.Add));
                count.Should().Be(1024 * 1024 * 16 + 15);

                progressReported.Should().ContainInOrder(Enumerable.Range(1, 100));
            }
        }

        [Theory]
        [InlineData("https://127.0.0.1:{port}")]
        [InlineData("https://127.0.0.1:{port}/")]
        [InlineData("https://localhost:{port}")]
        [InlineData("https://localhost:{port}/")]
        [InlineData("https://{machine}:{port}")]
        [InlineData("https://{machine}:{port}/")]
        public void SupportsHttpsGet(string uriFormat)
        {
            using (var octopus = new HalibutRuntime(services, Certificates.Octopus))
            {
                var listenPort = octopus.Listen();
                var uri = uriFormat.Replace("{machine}", Environment.MachineName).Replace("{port}", listenPort.ToString());

                var result = DownloadStringIgnoringCertificateValidation(uri);

                result.Should().Be("<html><body><p>Hello!</p></body></html>");
            }
        }

        [Theory]
        [InlineData("<html><body><h1>Welcome to Octopus Server!</h1><p>It looks like everything is running just like you expected, well done.</p></body></html>", null)]
        [InlineData("Simple text works too!", null)]
        [InlineData("", null)]
        [InlineData(null, "<html><body><p>Hello!</p></body></html>")]
        public void CanSetCustomFriendlyHtmlPage(string html, string expectedResult = null)
        {
            expectedResult = expectedResult ?? html; // Handle the null case which reverts to default html

            using (var octopus = new HalibutRuntime(services, Certificates.Octopus))
            {
                octopus.SetFriendlyHtmlPageContent(html);
                var listenPort = octopus.Listen();

                var result = DownloadStringIgnoringCertificateValidation("https://localhost:" + listenPort);

                result.Should().Be(expectedResult);
            }
        }

        [Fact]
        public void CanSetCustomFriendlyHtmlPageHeaders()
        {
            using (var octopus = new HalibutRuntime(services, Certificates.Octopus))
            {
                octopus.SetFriendlyHtmlPageHeaders(new Dictionary<string, string> {{"X-Content-Type-Options", "nosniff"}, {"X-Frame-Options", "DENY"}});
                var listenPort = octopus.Listen();

                var result = GetHeadersIgnoringCertificateValidation("https://localhost:" + listenPort).ToList();

                result.Should().Contain(x => x.Key == "X-Content-Type-Options" && x.Value == "nosniff");
                result.Should().Contain(x => x.Key == "X-Frame-Options" && x.Value == "DENY");
            }
        }

        [Fact]
        [Description("Connecting over a non-secure connection should cause the socket to be closed by the server. The socket used to be held open indefinitely for any failure to establish an SslStream.")]
        public void ConnectingOverHttpShouldFailQuickly()
        {
            var task = Task.Run(() => DoConnectingOverHttpShouldFailQuickly());
            if (!task.Wait(5000))
            {
                Assert.True(false, "Test did not complete within timeout");
            }
        }

        void DoConnectingOverHttpShouldFailQuickly()
        {
            using (var octopus = new HalibutRuntime(services, Certificates.Octopus))
            {
                var listenPort = octopus.Listen();
#if NET40
                Assert.Throws<WebException>(() => DownloadStringIgnoringCertificateValidation("http://localhost:" + listenPort));
#else
                Assert.Throws<HttpRequestException>(() => DownloadStringIgnoringCertificateValidation("http://localhost:" + listenPort));
#endif

            }
        }

        static string DownloadStringIgnoringCertificateValidation(string uri)
        {
#if NET40
            using (var webClient = new WebClient())
            {
                try
                {
                    // We need to ignore server certificate validation errors - the server certificate is self-signed
                    ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
                    return webClient.DownloadString(uri);
                }
                finally
                {
                    // And restore it back to default behaviour
                    ServicePointManager.ServerCertificateValidationCallback = null;
                }
            }
#else
            var handler = new HttpClientHandler();
            // We need to ignore server certificate validation errors - the server certificate is self-signed
            handler.ServerCertificateCustomValidationCallback = (sender, certificate, chain, errors) => true;
            using (var webClient = new HttpClient(handler))
            {
                return webClient.GetStringAsync(uri).GetAwaiter().GetResult();
            }
#endif
        }

        static IEnumerable<KeyValuePair<string, string>> GetHeadersIgnoringCertificateValidation(string uri)
        {
#if NET40
            using (var webClient = new WebClient())
            {
                try
                {
                    // We need to ignore server certificate validation errors - the server certificate is self-signed
                    ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
                    var response = webClient.DownloadString(uri);
                    foreach (string key in webClient.ResponseHeaders)
                    {
                        yield return new KeyValuePair<string, string>(key, webClient.ResponseHeaders[key]);
                    }
                }
                finally
                {
                    // And restore it back to default behaviour
                    ServicePointManager.ServerCertificateValidationCallback = null;
                }
            }
#else
            var handler = new HttpClientHandler();
            // We need to ignore server certificate validation errors - the server certificate is self-signed
            handler.ServerCertificateCustomValidationCallback = (sender, certificate, chain, errors) => true;
            using (var webClient = new HttpClient(handler))
            {
                var response = webClient.GetAsync(uri).GetAwaiter().GetResult();
                return response.Headers.Select(x => new KeyValuePair<string, string>(x.Key, string.Join(";", x.Value)));
            }
#endif
        }

#if HAS_SERVICE_POINT_MANAGER
        static void AddSslCertToLocalStoreAndRegisterFor(string address)
        {
            var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            store.Add(Certificates.Ssl);
            store.Close();


            var proc = new Process()
            {
                StartInfo = new ProcessStartInfo("netsh", $"http add sslcert ipport={address} certhash={Certificates.SslThumbprint} appid={{2e282bfb-fce9-40fc-a594-2136043e1c8f}}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            proc.Start();
            proc.WaitForExit();
            var output = proc.StandardOutput.ReadToEnd();

            if (proc.ExitCode != 0 && !output.Contains("Cannot create a file when that file already exists"))
            {
                Console.WriteLine(output);
                Console.WriteLine(proc.StandardError.ReadToEnd());
                throw new Exception("Could not bind cert to port");
            }
        }

        static void RemoveSslCertBindingFor(string address)
        {
            var proc = new Process()
            {
                StartInfo = new ProcessStartInfo("netsh", $"http delete sslcert ipport={address}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            proc.Start();
            proc.WaitForExit();
            var output = proc.StandardOutput.ReadToEnd();

            if (proc.ExitCode != 0)
            {
                Console.WriteLine(output);
                Console.WriteLine(proc.StandardError.ReadToEnd());
                throw new Exception("The system cannot find the file specified");
            }
        }
#endif
    }
}