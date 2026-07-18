using System.Net;
using System.Text;
using System.Text.Json;
using SilverScreen.Core.Models;
using SilverScreen.Infrastructure.Features.Session;
using SilverScreen.Infrastructure.YouTube;

namespace SilverScreen.Tests;

public sealed class YouTubeHomeClientTests
{
    private const string BootstrapHtml = """
                                         <html>
                                         <head>
                                           <script>
                                             var ytcfg = {
                                               "INNERTUBE_API_KEY": "fake-api-key-123",
                                               "INNERTUBE_CONTEXT_CLIENT_VERSION": "1.20260710.01.00",
                                               "VISITOR_DATA": "fake-visitor-data-abc"
                                             };
                                           </script>
                                         </head>
                                         </html>
                                         """;

    private static string CreateNetscapeCookieFile(params (string Name, string Value)[] cookies)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Netscape HTTP Cookie File");
        foreach (var cookie in cookies)
            sb.AppendLine($"youtube.com\tTRUE\t/\tTRUE\t2147483647\t{cookie.Name}\t{cookie.Value}");

        return sb.ToString();
    }

    [Fact]
    public void GenerateSapisidHash_ReturnsExpectedSha1Hex()
    {
        // Arrange
        var netscapeContent = CreateNetscapeCookieFile(("SAPISID", "fake-sapisid"));
        var credentials = YouTubeCredentials.ParseNetscape(netscapeContent);
        Assert.NotNull(credentials);

        // Act
        var hash = credentials.GenerateSapisidHash(1700000000L);

        // Assert
        Assert.Equal("6b2c32afdc7a2c4f00b844c84a58147e96fba5d6", hash);
    }

    [Fact]
    public void ParseNetscape_PrioritizesSecureSapisid()
    {
        // Arrange
        var content = CreateNetscapeCookieFile(
            ("SAPISID", "regular-sapisid"),
            ("__Secure-3PAPISID", "secure-sapisid")
        );

        // Act
        var credentials = YouTubeCredentials.ParseNetscape(content);

        // Assert
        Assert.NotNull(credentials);
        Assert.Equal("secure-sapisid", credentials.Sapisid);
    }

    [Fact]
    public void ParseNetscape_HandlesHttpOnlyCookiesAndPrefersSecureSapisid()
    {
        // Arrange
        var content = """
                      # Netscape HTTP Cookie File
                      # This is an ordinary comment that must be ignored
                      #HttpOnly_.youtube.com	TRUE	/	TRUE	2147483647	__Secure-3PAPISID	secure-sapisid-value
                      .youtube.com	TRUE	/	TRUE	2147483647	SAPISID	regular-sapisid-value
                      youtube.com	TRUE	/	FALSE	2147483647	SID	normal-youtube-cookie-value
                      """;

        // Act
        var credentials = YouTubeCredentials.ParseNetscape(content);

        // Assert
        Assert.NotNull(credentials);
        Assert.Equal("secure-sapisid-value", credentials.Sapisid);
        Assert.Equal(
            "__Secure-3PAPISID=secure-sapisid-value; SAPISID=regular-sapisid-value; SID=normal-youtube-cookie-value",
            credentials.CookieHeader);
    }

    [Fact]
    public void ParseNetscape_MinimizesCookieHeaderByExcludingUnrelatedCookies()
    {
        // Arrange
        var content = CreateNetscapeCookieFile(
            ("SID", "fake-sid-value"),
            ("HSID", "fake-hsid-value"),
            ("LOGIN_INFO", "fake-login-info-value"),
            ("CONSENT", "fake-consent-value"),
            ("SOCS", "fake-socs-value"),
            ("SSID", "fake-ssid-value"),
            ("APISID", "fake-apisid-value"),
            ("SAPISID", "fake-sapisid-value"),
            ("__Secure-1PAPISID", "fake-1papisid-value"),
            ("__Secure-3PAPISID", "fake-3papisid-value"),
            ("__Secure-1PSID", "fake-1psid-value"),
            ("__Secure-3PSID", "fake-3psid-value"),
            ("__Secure-1PSIDTS", "fake-1psidts-value"),
            ("__Secure-3PSIDTS", "fake-3psidts-value"),
            ("SIDCC", "fake-sidcc-value"),
            ("__Secure-1PSIDCC", "fake-1psidcc-value"),
            ("__Secure-3PSIDCC", "fake-3psidcc-value"),
            ("PREF", "f6=80&tz=America.Chicago&f5=30000"),
            ("VISITOR_INFO1_LIVE", "fake-visitor-info-value-which-is-large")
        );

        // Act
        var credentials = YouTubeCredentials.ParseNetscape(content);

        // Assert
        Assert.NotNull(credentials);

        // Assert that the unrelated/large cookies are omitted
        Assert.DoesNotContain("PREF", credentials.CookieHeader);
        Assert.DoesNotContain("VISITOR_INFO1_LIVE", credentials.CookieHeader);

        // Assert that the required authentication cookies are retained
        Assert.Contains("SID=fake-sid-value", credentials.CookieHeader);
        Assert.Contains("HSID=fake-hsid-value", credentials.CookieHeader);
        Assert.Contains("LOGIN_INFO=fake-login-info-value", credentials.CookieHeader);
        Assert.Contains("CONSENT=fake-consent-value", credentials.CookieHeader);
        Assert.Contains("SOCS=fake-socs-value", credentials.CookieHeader);
        Assert.Contains("SSID=fake-ssid-value", credentials.CookieHeader);
        Assert.Contains("APISID=fake-apisid-value", credentials.CookieHeader);
        Assert.Contains("SAPISID=fake-sapisid-value", credentials.CookieHeader);
        Assert.Contains("__Secure-1PAPISID=fake-1papisid-value", credentials.CookieHeader);
        Assert.Contains("__Secure-3PAPISID=fake-3papisid-value", credentials.CookieHeader);
        Assert.Contains("__Secure-1PSID=fake-1psid-value", credentials.CookieHeader);
        Assert.Contains("__Secure-3PSID=fake-3psid-value", credentials.CookieHeader);
        Assert.Contains("__Secure-1PSIDTS=fake-1psidts-value", credentials.CookieHeader);
        Assert.Contains("__Secure-3PSIDTS=fake-3psidts-value", credentials.CookieHeader);
        Assert.Contains("SIDCC=fake-sidcc-value", credentials.CookieHeader);
        Assert.Contains("__Secure-1PSIDCC=fake-1psidcc-value", credentials.CookieHeader);
        Assert.Contains("__Secure-3PSIDCC=fake-3psidcc-value", credentials.CookieHeader);

        // Confirm full CookieHeader does not contain any of the unrelated cookies and contains only the minimized auth cookies
        var expectedCookieHeader =
            "SID=fake-sid-value; HSID=fake-hsid-value; LOGIN_INFO=fake-login-info-value; CONSENT=fake-consent-value; SOCS=fake-socs-value; SSID=fake-ssid-value; APISID=fake-apisid-value; SAPISID=fake-sapisid-value; " +
            "__Secure-1PAPISID=fake-1papisid-value; __Secure-3PAPISID=fake-3papisid-value; __Secure-1PSID=fake-1psid-value; " +
            "__Secure-3PSID=fake-3psid-value; __Secure-1PSIDTS=fake-1psidts-value; __Secure-3PSIDTS=fake-3psidts-value; " +
            "SIDCC=fake-sidcc-value; __Secure-1PSIDCC=fake-1psidcc-value; __Secure-3PSIDCC=fake-3psidcc-value";
        Assert.Equal(expectedCookieHeader, credentials.CookieHeader);
    }

    [Fact]
    public void Credentials_ToString_RedactsSecrets()
    {
        // Arrange
        var netscapeContent = CreateNetscapeCookieFile(("SAPISID", "supersecret123"));
        var credentials = YouTubeCredentials.ParseNetscape(netscapeContent);

        // Act
        var toStringValue = credentials!.ToString();

        // Assert
        Assert.Equal("YouTubeCredentials [REDACTED]", toStringValue);
        Assert.DoesNotContain("supersecret123", toStringValue);
    }

    [Fact]
    public void BootstrapConfig_ToString_RedactsSecrets()
    {
        // Arrange
        var config = new YouTubeBootstrapConfig("secret-api-key", "1.2.3", "secret-visitor-data");

        // Act
        var toStringValue = config.ToString();

        // Assert
        Assert.Contains("1.2.3", toStringValue);
        Assert.Contains("[REDACTED]", toStringValue);
        Assert.DoesNotContain("secret-api-key", toStringValue);
        Assert.DoesNotContain("secret-visitor-data", toStringValue);
    }

    [Fact]
    public async Task GetHomeFeedAsync_NoSession_ReturnsRequiresAuthenticationWithoutHttpCall()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        var handler = new FakeHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, sessionService);

        // Act
        var result = await client.GetHomeFeedAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.RequiresAuthentication);
        Assert.Equal("Authentication session not found.", result.StatusMessage);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetHomeFeedAsync_InvalidSessionCookies_ReturnsRequiresAuthenticationWithoutHttpCall()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession("invalid cookie content", SessionCookieFormat.NetscapeCookiesText);
        var handler = new FakeHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, sessionService);

        // Act
        var result = await client.GetHomeFeedAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.RequiresAuthentication);
        Assert.Equal("Incomplete authentication credentials. Missing required session cookies.", result.StatusMessage);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetHomeFeedAsync_StandardVideoRenderer_ParsesAndMapsCorrectly()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);

        const string responseJson = """
                                    {
                                      "contents": {
                                        "twoColumnBrowseResultsRenderer": {
                                          "tabs": [
                                            {
                                              "tabRenderer": {
                                                "content": {
                                                  "richGridRenderer": {
                                                    "contents": [
                                                      {
                                                        "richItemRenderer": {
                                                          "content": {
                                                            "videoRenderer": {
                                                              "videoId": "video1",
                                                              "title": "Title 1",
                                                              "ownerText": {
                                                                "simpleText": "Channel A"
                                                              },
                                                              "lengthText": {
                                                                "runs": [
                                                                  { "text": "10" },
                                                                  { "text": ":" },
                                                                  { "text": "15" }
                                                                ]
                                                              },
                                                              "thumbnail": {
                                                                "thumbnails": [
                                                                  { "url": "https://example.com/low.jpg" },
                                                                  { "url": "https://example.com/high.jpg" }
                                                                ]
                                                              }
                                                            }
                                                          }
                                                        }
                                                      },
                                                      {
                                                        "richItemRenderer": {
                                                          "content": {
                                                            "videoRenderer": {
                                                              "videoId": "video2",
                                                              "title": {
                                                                "runs": [
                                                                  { "text": "Title" },
                                                                  { "text": " " },
                                                                  { "text": "2" }
                                                                ]
                                                              },
                                                              "shortBylineText": {
                                                                "simpleText": "Channel B"
                                                              },
                                                              "lengthText": {
                                                                "simpleText": "1:02:03"
                                                              },
                                                              "thumbnail": {
                                                                "thumbnails": [
                                                                  { "url": "https://example.com/med.jpg" }
                                                                ]
                                                              }
                                                            }
                                                          }
                                                        }
                                                      },
                                                      {
                                                        "richItemRenderer": {
                                                          "content": {
                                                            "videoRenderer": {
                                                              "videoId": "video3",
                                                              "title": "Title 3",
                                                              "longBylineText": {
                                                                "simpleText": "Channel C"
                                                              },
                                                              "lengthText": {
                                                                "simpleText": "45"
                                                              }
                                                            }
                                                          }
                                                        }
                                                      }
                                                    ]
                                                  }
                                                }
                                              }
                                            }
                                          ]
                                        }
                                      }
                                    }
                                    """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BootstrapHtml)
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, sessionService);

        // Act
        var result = await client.GetHomeFeedAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Videos.Count);

        // Video 1
        var v1 = result.Videos[0];
        Assert.Equal("video1", v1.Id);
        Assert.Equal("Title 1", v1.Title);
        Assert.Equal("Channel A", v1.ChannelName);
        Assert.Equal(new TimeSpan(0, 10, 15), v1.Duration);
        Assert.Equal("https://example.com/high.jpg", v1.ThumbnailUrl);
        Assert.Equal("https://www.youtube.com/watch?v=video1", v1.WatchUrl);
        Assert.False(v1.IsShort);

        // Video 2
        var v2 = result.Videos[1];
        Assert.Equal("video2", v2.Id);
        Assert.Equal("Title 2", v2.Title);
        Assert.Equal("Channel B", v2.ChannelName);
        Assert.Equal(new TimeSpan(1, 2, 3), v2.Duration);
        Assert.Equal("https://example.com/med.jpg", v2.ThumbnailUrl);

        // Video 3
        var v3 = result.Videos[2];
        Assert.Equal("video3", v3.Id);
        Assert.Equal("Title 3", v3.Title);
        Assert.Equal("Channel C", v3.ChannelName);
        Assert.Equal(TimeSpan.FromSeconds(45), v3.Duration);
        Assert.Equal("", v3.ThumbnailUrl);
    }

    [Fact]
    public async Task GetHomeFeedAsync_ShortsFiltering_FiltersShortsViewModelAndReelEndpoints()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);

        const string responseJson = """
                                    {
                                      "contents": {
                                        "items": [
                                          {
                                            "shortsLockupViewModel": {
                                              "entityId": "short1"
                                            }
                                          },
                                          {
                                            "videoRenderer": {
                                              "videoId": "short_video_1",
                                              "title": "Short Video",
                                              "reelEndpoint": {
                                                "reelWatchEndpoint": {
                                                  "videoId": "short_video_1"
                                                }
                                              }
                                            }
                                          },
                                          {
                                            "reelEndpoint": {
                                              "videoId": "short_video_2"
                                            }
                                          },
                                          {
                                            "videoRenderer": {
                                              "videoId": "normal_video_1",
                                              "title": "Normal Video"
                                            }
                                          }
                                        ]
                                      }
                                    }
                                    """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BootstrapHtml)
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, sessionService);

        // Act
        var result = await client.GetHomeFeedAsync();

        // Assert
        Assert.True(result.IsSuccess);
        var video = Assert.Single(result.Videos);
        Assert.Equal("normal_video_1", video.Id);
    }

    [Fact]
    public async Task GetHomeFeedAsync_AdsAndPromotedFiltering_FiltersAdRenderers()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);

        const string responseJson = """
                                    {
                                      "contents": {
                                        "items": [
                                          {
                                            "adSlotRenderer": {
                                              "videoRenderer": {
                                                "videoId": "ad_video_1",
                                                "title": "Ad Video 1"
                                              }
                                            }
                                          },
                                          {
                                            "adPlacementRenderer": {
                                              "videoRenderer": {
                                                "videoId": "ad_video_2",
                                                "title": "Ad Video 2"
                                              }
                                            }
                                          },
                                          {
                                            "promotedVideoRenderer": {
                                              "videoId": "promoted_video_1",
                                              "title": "Promoted Video 1"
                                            }
                                          },
                                          {
                                            "advertisementRenderer": {
                                              "videoRenderer": {
                                                "videoId": "ad_video_3",
                                                "title": "Ad Video 3"
                                              }
                                            }
                                          },
                                          {
                                            "videoRenderer": {
                                              "videoId": "normal_video_2",
                                              "title": "Normal Video 2"
                                            }
                                          }
                                        ]
                                      }
                                    }
                                    """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BootstrapHtml)
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, sessionService);

        // Act
        var result = await client.GetHomeFeedAsync();

        // Assert
        Assert.True(result.IsSuccess);
        var video = Assert.Single(result.Videos);
        Assert.Equal("normal_video_2", video.Id);
    }

    [Fact]
    public async Task GetHomeFeedAsync_MissingFieldsAndUnknownRenderers_SafelyIgnored()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);

        const string responseJson = """
                                    {
                                      "contents": {
                                        "items": [
                                          {
                                            "videoRenderer": {
                                              "title": "Missing Video ID"
                                            }
                                          },
                                          {
                                            "videoRenderer": {
                                              "videoId": ""
                                            }
                                          },
                                          {
                                            "channelRenderer": {
                                              "channelId": "chan1",
                                              "title": "Some Channel"
                                            }
                                          },
                                          {
                                            "unknownRenderer": {
                                              "someProp": "someVal"
                                            }
                                          },
                                          {
                                            "videoRenderer": {
                                              "videoId": "valid_video_3",
                                              "title": "Valid Video"
                                            }
                                          }
                                        ]
                                      }
                                    }
                                    """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BootstrapHtml)
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, sessionService);

        // Act
        var result = await client.GetHomeFeedAsync();

        // Assert
        Assert.True(result.IsSuccess);
        var video = Assert.Single(result.Videos);
        Assert.Equal("valid_video_3", video.Id);
    }

    [Fact]
    public async Task GetHomeFeedAsync_ExtractsContinuationToken()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);

        const string responseJson = """
                                    {
                                      "contents": {
                                        "continuationCommand": {
                                          "token": "next_continuation_token_value_abc"
                                        }
                                      }
                                    }
                                    """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BootstrapHtml)
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, sessionService);

        // Act
        var result = await client.GetHomeFeedAsync();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("next_continuation_token_value_abc", result.ContinuationToken);
    }

    [Fact]
    public async Task GetHomeFeedAsync_WithContinuationToken_SendsContinuationInPayload()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BootstrapHtml)
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, sessionService);

        // Act
        var result = await client.GetHomeFeedAsync("continuation_token_123");

        // Assert
        Assert.True(result.IsSuccess);

        var postIndex = -1;
        for (var i = 0; i < handler.Requests.Count; i++)
            if (handler.Requests[i].Method == HttpMethod.Post)
            {
                postIndex = i;
                break;
            }

        Assert.True(postIndex >= 0);
        var requestBodyJson = handler.RequestBodies[postIndex];
        using var doc = JsonDocument.Parse(requestBodyJson);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("continuation", out var contProp));
        Assert.Equal("continuation_token_123", contProp.GetString());
        Assert.False(root.TryGetProperty("browseId", out _));
    }

    [Fact]
    public async Task GetHomeFeedAsync_InvalidJsonResponse_ReturnsControlledFailure()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BootstrapHtml)
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ invalid json")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, sessionService);

        // Act
        var result = await client.GetHomeFeedAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Failed to parse the YouTube home feed JSON response.", result.StatusMessage);
        Assert.False(result.RequiresAuthentication);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task GetHomeFeedAsync_HttpErrorResponse_HandlesRequiresAuthenticationCorrectly(
        HttpStatusCode statusCode, bool expectedRequiresAuth)
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BootstrapHtml)
                };

            return new HttpResponseMessage(statusCode);
        });

        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, sessionService);

        // Act
        var result = await client.GetHomeFeedAsync();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(expectedRequiresAuth, result.RequiresAuthentication);
        Assert.Equal($"YouTube InnerTube API returned HTTP status {(int)statusCode}.", result.StatusMessage);
    }

    [Fact]
    public async Task GetHomeFeedAsync_InvokesTimeSourceAndSendsCorrectAuthorizationHeaderFormat()
    {
        // Arrange
        var sessionService = new InMemorySessionService();
        sessionService.SetManualSession(CreateNetscapeCookieFile(("SAPISID", "fake-sapisid")),
            SessionCookieFormat.NetscapeCookiesText);

        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BootstrapHtml)
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = new YouTubeHomeClient(httpClient, sessionService)
        {
            TimeSource = () => 1700000000L
        };

        // Act
        var result = await client.GetHomeFeedAsync();

        // Assert
        Assert.True(result.IsSuccess);

        var postRequest = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);

        // Assert header exists without asserting/exposing the value in the test output
        Assert.NotNull(postRequest.Headers.Authorization);
        Assert.Equal("SAPISIDHASH", postRequest.Headers.Authorization.Scheme);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _handler;
        private readonly List<string> _requestBodies = new();
        private readonly List<HttpRequestMessage> _requests = new();

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage>? handler = null)
        {
            _handler = handler;
        }

        public IReadOnlyList<HttpRequestMessage> Requests => _requests;
        public IReadOnlyList<string> RequestBodies => _requestBodies;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add(request);
            if (request.Content != null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                _requestBodies.Add(body);
            }
            else
            {
                _requestBodies.Add(string.Empty);
            }

            if (_handler != null) return _handler(request);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}