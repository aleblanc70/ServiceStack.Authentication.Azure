﻿using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Moq;
using ServiceStack.Auth;
using ServiceStack.Authentication.Azure.OrmLite;
using ServiceStack.Authentication.Azure.ServiceModel;
using ServiceStack.Authentication.Azure.ServiceModel.Entities;
using ServiceStack.Authentication.Azure20.Tests;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using ServiceStack.Testing;
using ServiceStack.Web;
using Xunit;

namespace ServiceStack.Authentication.Azure.Tests
{
    public class AzureGraphAuthProviderTests : IDisposable
    {
        #region Constants and Variables

        //private static ServiceStackHost _appHost;

        #endregion

        #region Constructors

        public AzureGraphAuthProviderTests()
        {

        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {

        }

        #endregion

        #region Public/Internal

        //[Fact, Order(-1)]
        // public ServiceStackHost InitServer(Action<IRequest,IResponse,object> responseFilter = null)
        // {
        //     return new BasicAppHost
        //     {
        //         ConfigureAppHost = host =>
        //         {
        //             if (responseFilter != null)
        //                 host.GlobalResponseFilters.Add(responseFilter);

        //             host.OnEndRequestCallbacks.Add(request =>
        //             {

        //             });
        //             host.Plugins.Add(
        //                 new AuthFeature(() => new AuthUserSession(),
        //                     new IAuthProvider[]
        //                     {
        //                         new AzureAuthenticationProvider(new TestAzureGraphService())
        //                         {
        //                         }
        //                     }));

        //             var container = host.GetContainer();
        //             container.Register<IDbConnectionFactory>(
        //                 new OrmLiteConnectionFactory(":memory:", SqliteDialect.Provider));
        //             container.Register<IApplicationRegistryService>(
        //                 c => new OrmLiteMultiTenantApplicationRegistryService(c.Resolve<IDbConnectionFactory>()));
        //             container.Register<IAuthRepository>(
        //                 c => new OrmLiteAuthRepository(c.Resolve<IDbConnectionFactory>()));


        //             host.AfterInitCallbacks.Add(appHost =>
        //             {
        //                 var authRepo = appHost.TryResolve<IAuthRepository>();
        //                 authRepo.InitSchema();

        //                 var regService = host.TryResolve<IApplicationRegistryService>();
        //                 regService.InitSchema();
        //                 regService.RegisterApplication(new ApplicationRegistration
        //                 {
        //                     ClientId = "clientid",
        //                     ClientSecret = "clientsecret",
        //                     DirectoryName = "foodomain.com"
        //                 });
        //             });
        //         }
        //     }.Init();
        // }

        [Fact, Order(5)]
        public void ShouldHaveRegisteredAuthenticateService()
        {
            var subject = TestServer.Current.TryResolve<AuthenticateService>();
            Assert.NotNull(subject);
        }

        [Fact, Order(5)]
        public void ShouldBeAuthProvider()
        {
            var subject = new AzureAuthenticationProvider(new TestAzureGraphService());
            Assert.IsAssignableFrom<AuthProvider>(subject);
            Assert.Equal("ms-graph", subject.Provider);
        }

        [Theory, Order(5)]
        [InlineData("custom-clientid", "custom-clientsecret", "custom-foodomain.com", null)]
        [InlineData("custom-clientid", "custom-clientsecret", "custom-foodomain.com", "joe@custom-foodomain.com")]
        public void ShouldRequestCodeWithCustomDirectoryResolver(string clientId, string clientSecret
            , string directoryName, string username)
        {
            var appHost = TestServer.Current;
            var directory = new ApplicationRegistration
            {
                ClientId = clientId,
                ClientSecret = clientSecret,
                DirectoryName = directoryName
            };

            var subject = new AzureAuthenticationProvider(new TestAzureGraphService())
            {
                ApplicationDirectoryResolver = (serviceBase, registryService, session) => directory
            };

            var auth = new Authenticate()
            {
                UserName = username
            };

            var authService = MockAuthService(appHost: appHost);
            var response = subject.Authenticate(authService.Object, new AuthUserSession(), auth);

            var result = (IHttpResult)response;
            if (string.IsNullOrWhiteSpace(username))
            {
                Assert.StartsWith($"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={clientId}&response_type=code&redirect_uri=http%3a%2f%2flocalhost%2f&scope=https%3a%2f%2fgraph.microsoft.com%2fUser.Read+offline%5faccess+openid+profile", result.Headers["Location"]);
            }
            else
            {
                Assert.StartsWith($"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={clientId}&response_type=code&redirect_uri=http%3a%2f%2flocalhost%2f&domain_hint={username}&scope=https%3a%2f%2fgraph.microsoft.com%2fUser.Read+offline%5faccess+openid+profile", result.Headers["Location"]);
            }

            var codeRequest = new Uri(result.Headers["Location"]);
            var query = new NameValueCollection();
            Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(codeRequest.Query)
                .ForEach((key, values) => query.Add(key, values.ToString()));
            if (!string.IsNullOrWhiteSpace(username))
            {
                Assert.Equal(username, query["domain_hint"]);
            }
            Assert.Equal("code", query["response_type"]);
            Assert.Equal(query["client_id"], clientId);
            Assert.Equal(query["redirect_uri"].UrlDecode(), subject.CallbackUrl);
        }

         [Fact, Order(5)]
         public void ShouldRequestCode()
         {
             var appHost = TestServer.Current;
             var subject = new AzureAuthenticationProvider(new TestAzureGraphService());
             var auth = new Authenticate
             {
                 UserName = "some.user@foodomain.com"
             };


             var authService = MockAuthService(appHost: appHost);
             var response = subject.Authenticate(authService.Object, new AuthUserSession(), auth);

             var result = (IHttpResult)response;
             Assert.StartsWith("https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id=clientid&response_type=code&redirect_uri=http%3a%2f%2flocalhost%2f&domain_hint=some.user@foodomain.com&scope=https%3a%2f%2fgraph.microsoft.com%2fUser.Read", result.Headers["Location"]);
             var codeRequest = new Uri(result.Headers["Location"]);
             var query = PclExportClient.Instance.ParseQueryString(codeRequest.Query);
             Assert.Equal("code", query["response_type"]);
             Assert.Equal("clientid", query["client_id"]);
             Assert.Equal(query["redirect_uri"].UrlDecode(), subject.CallbackUrl);
             Assert.Equal("some.user@foodomain.com", query["domain_hint"]);

         }

        [Fact, Order(5)]
        public void ShouldSetCallbackUrlWithoutParameters()
        {
            var appHost = TestServer.Current;
            var subject = new AzureAuthenticationProvider(new TestAzureGraphService());
            var auth = new Authenticate
            {
                UserName = "some.user@foodomain.com"
            };

            var request = new MockHttpRequest("auth", "GET", "text",
                "/auth/foo/bar?redirect=" + "http://localhost/secure-resource".UrlEncode(), new NameValueCollection
                {
                        {"redirect", "http://localhost/secure-resource"}
                }, Stream.Null, null);
            var mockAuthService = MockAuthService(request, appHost);

            var response = subject.Authenticate(mockAuthService.Object, new AuthUserSession(), auth);

            var result = (IHttpResult)response;
            var codeRequest = new Uri(result.Headers["Location"]);
            var query = PclExportClient.Instance.ParseQueryString(codeRequest.Query);
            Assert.Equal("code", query["response_type"]);
            Assert.Equal("http://localhost/auth/foo/bar", subject.CallbackUrl);
            Assert.Equal(query["redirect_uri"].UrlDecode(), subject.CallbackUrl);
        }

        [Fact, Order(5)]
        public void ShouldRedirectToFailurePathIfErrorIn()
        {
            var appHost = TestServer.Current;
            // See https://tools.ietf.org/html/rfc6749#section-4.1.2.1
            var subject = new AzureAuthenticationProvider(new TestAzureGraphService())
            {
                FailureRedirectPath = "/auth-failure"
            };
            var auth = new Authenticate
            {
                UserName = "some.user@foodomain.com"
            };

            var request = new MockHttpRequest("auth", "GET", "text", "/auth/foo?error=invalid_request",
                new NameValueCollection { { "error", "invalid_request" } }, Stream.Null, new NameValueCollection());
            var mockAuthService = MockAuthService(request, appHost);

            var response = subject.Authenticate(mockAuthService.Object, new AuthUserSession(), auth);

            var result = (IHttpResult)response;
            //var redirectRequest = new Uri(result.Headers["Location"]);
            Assert.Equal("http://localhost/auth-failure?f=invalid_request&response_type=code",
                result.Headers["Location"]);
            var query = PclExportClient.Instance.ParseQueryString(new Uri(result.Headers["Location"]).Query);
            Assert.Equal("code", query["response_type"]);
        }

        [Fact, Order(5)]
        public void ShouldAuthenticateUser()
        {
            //            var service = _appHost.Resolve<AuthenticateService>();
            //            Assert.NotNull(service);
            //
            //            var auth = new Authenticate
            //            {
            //                provider = "ms-graph",
            //                UserName = "some.user@foodomain.com"
            //            };
            //            service.Request = new MockHttpRequest
            //            {
            //            };
            //
            //            var result = service.Post(auth);
            //            Assert.NotNull(result);
        }

        [Theory, Order(5)]
        [InlineData("joe@custom-foodomain.com")]
        [InlineData(null)]
        public void ShouldRequestTokenWithCustomDirectoryResolver(string username)
        {
            var appHost = TestServer.Current;
            var directory = new ApplicationRegistration
            {
                ClientId = "custom-clientid",
                ClientSecret = "custom-clientsecret",
                DirectoryName = "custom-foodomain.com"
            };
            var auth = new Authenticate { UserName = username };

            var subject = new AzureAuthenticationProvider(new TestAzureGraphService())
            {
                ApplicationDirectoryResolver = (serviceBase, registryService, s) => directory,

            };

            var session = new AuthUserSession
            {
                State = "D79E5777-702E-4260-9A62-37F75FF22CCE",
                UserName = auth.UserName
            };

            subject.CallbackUrl = "http://localhost/myapp/";
            var request = new MockHttpRequest("myapp", "GET", "text", "/myapp", new NameValueCollection
                {
                    {
                        "code",
                        "AwABAAAAvPM1KaPlrEqdFSBzjqfTGBCmLdgfSTLEMPGYuNHSUYBrqqf_ZT_p5uEAEJJ_nZ3UmphWygRNy2C3jJ239gV_DBnZ2syeg95Ki-374WHUP-i3yIhv5i-7KU2CEoPXwURQp6IVYMw-DjAOzn7C3JCu5wpngXmbZKtJdWmiBzHpcO2aICJPu1KvJrDLDP20chJBXzVYJtkfjviLNNW7l7Y3ydcHDsBRKZc3GuMQanmcghXPyoDg41g8XbwPudVh7uCmUponBQpIhbuffFP_tbV8SNzsPoFz9CLpBCZagJVXeqWoYMPe2dSsPiLO9Alf_YIe5zpi-zY4C3aLw5g9at35eZTfNd0gBRpR5ojkMIcZZ6IgAA"
                    },
                    {"session_state", "7B29111D-C220-4263-99AB-6F6E135D75EF"},
                    {"state", "D79E5777-702E-4260-9A62-37F75FF22CCE"}
                }, Stream.Null, new NameValueCollection());

            var mockAuthService = MockAuthService(request, appHost);
            var response = (IHttpResult)subject.Authenticate(mockAuthService.Object, session, auth);
            Assert.True(session.IsAuthenticated);
            var tokens = session.GetAuthTokens("ms-graph");
            Assert.NotNull(tokens);
            Assert.Equal("ms-graph", tokens.Provider);
            Assert.Equal(tokens.AccessTokenSecret, TokenHelper.AccessToken);
            Assert.NotNull(tokens.RefreshTokenExpiry);
            Assert.Equal(tokens.RefreshToken, TokenHelper.RefreshToken);

            // Regardless of what is entered up front, Azure will determine what the identity values are
            Assert.Equal("d542096aa0b94e2195856b57e43257e4", tokens.UserId); // oid
            Assert.Equal("some.user@foodomain.com", tokens.UserName);
            Assert.Equal("Some User", tokens.DisplayName);
            Assert.Equal(session.UserName, tokens.UserName);
            Assert.Equal(session.LastName, tokens.LastName);
            Assert.Equal(session.FirstName, tokens.FirstName);
            Assert.Equal(session.DisplayName, tokens.DisplayName);

            var result = (IHttpResult)response;
            Assert.StartsWith("http://localhost#s=1", result.Headers["Location"]);
        }

        [Fact, Order(5)]
        public void ShouldRequestToken()
        {
            var appHost = TestServer.Current;
            var subject = new AzureAuthenticationProvider(new TestAzureGraphService())
            {
            };

            var auth = new Authenticate
            {
                UserName = "some.user@foodomain.com"
            };
            var session = new AuthUserSession
            {
                State = "D79E5777-702E-4260-9A62-37F75FF22CCE",
                UserName = auth.UserName
            };

            subject.CallbackUrl = "http://localhost/myapp/";
            var request = new MockHttpRequest("myapp", "GET", "text", "/myapp", new NameValueCollection
                {
                    {
                        "code",
                        "AwABAAAAvPM1KaPlrEqdFSBzjqfTGBCmLdgfSTLEMPGYuNHSUYBrqqf_ZT_p5uEAEJJ_nZ3UmphWygRNy2C3jJ239gV_DBnZ2syeg95Ki-374WHUP-i3yIhv5i-7KU2CEoPXwURQp6IVYMw-DjAOzn7C3JCu5wpngXmbZKtJdWmiBzHpcO2aICJPu1KvJrDLDP20chJBXzVYJtkfjviLNNW7l7Y3ydcHDsBRKZc3GuMQanmcghXPyoDg41g8XbwPudVh7uCmUponBQpIhbuffFP_tbV8SNzsPoFz9CLpBCZagJVXeqWoYMPe2dSsPiLO9Alf_YIe5zpi-zY4C3aLw5g9at35eZTfNd0gBRpR5ojkMIcZZ6IgAA"
                    },
                    {"session_state", "7B29111D-C220-4263-99AB-6F6E135D75EF"},
                    {"state", "D79E5777-702E-4260-9A62-37F75FF22CCE"}
                }, Stream.Null, new NameValueCollection())
            {
                HttpMethod = "POST",
                Items =
                    {
                        {Keywords.Session, session}
                    }
            };
            var mockAuthService = MockAuthService(request, appHost);
            using (new HttpResultsFilter
            {
                StringResultFn = (tokenRequest, s) =>
                {
                    Assert.Equal("https://login.microsoftonline.com/common/oauth2/v2.0/token",
                        tokenRequest.RequestUri.ToString());
                    Assert.Equal("POST", tokenRequest.Method);
                    Assert.Equal("application/x-www-form-urlencoded", tokenRequest.ContentType);
                    return TokenHelper.GetIdToken();
                }
            })
            {
                //var response = authService.Post(auth);
                var response = (IHttpResult)subject.Authenticate(mockAuthService.Object, session, auth);
                Assert.True(session.IsAuthenticated);
                var tokens = session.GetAuthTokens("ms-graph");
                Assert.NotNull(tokens);
                Assert.Equal("ms-graph", tokens.Provider);
                Assert.Equal(tokens.AccessTokenSecret, TokenHelper.AccessToken);
                Assert.NotNull(tokens.RefreshTokenExpiry);
                Assert.Equal(tokens.RefreshToken, TokenHelper.RefreshToken);

                Assert.Equal("d542096aa0b94e2195856b57e43257e4", tokens.UserId); // oid
                Assert.Equal("some.user@foodomain.com", tokens.UserName);
                Assert.Equal("User", tokens.LastName);
                Assert.Equal("Some", tokens.FirstName);
                Assert.Equal("Some User", tokens.DisplayName);
                Assert.Equal(session.UserName, tokens.UserName);
                Assert.Equal(session.LastName, tokens.LastName);
                Assert.Equal(session.FirstName, tokens.FirstName);
                Assert.Equal(session.DisplayName, tokens.DisplayName);
                var result = (IHttpResult)response;
                Assert.StartsWith("http://localhost#s=1", result.Headers["Location"]);
            }
        }

        [Fact, Order(5)]
        public void ShouldSetReferrerFromRedirectParam()
        {
            var appHost = TestServer.Current;
            var subject = new AzureAuthenticationProvider(new TestAzureGraphService());
            var auth = new Authenticate
            {
                UserName = "some.user@foodomain.com"
            };

            var session = new AuthUserSession();
            var request = new MockHttpRequest("myapp", "GET", "text", "/myapp", new NameValueCollection
                {
                    {"redirect", "http://localhost/myapp/secure-resource"}
                }, Stream.Null, null)
            {
                Items =
                    {
                        {Keywords.Session, session}
                    }
            };
            //subject.Request = request;
            var mockAuthService = MockAuthService(request, appHost);

            var result = subject.Authenticate(mockAuthService.Object, session, auth); // subject.Post(auth);

            Assert.Equal("http://localhost/myapp/secure-resource", session.ReferrerUrl);
        }

        [Fact, Order(5)]
        public void ShouldNotAuthenticateIfDirectoryNameNotMatched()
        {
            var appHost = TestServer.Current;
            var subject = new AzureAuthenticationProvider(new TestAzureGraphService());
            var auth = new Authenticate
            {
                UserName = "some.user@foodomain.com"
            };
            subject.CallbackUrl = "http://localhost/myapp/";
            var request = new MockHttpRequest("myapp", "GET", "text", "/myapp", new NameValueCollection
                {
                    {"code", "code123"},
                    {"state", "D79E5777-702E-4260-9A62-37F75FF22CCE"}
                }, Stream.Null, new NameValueCollection());
            var mockAuthService = MockAuthService(request, appHost);
            using (new HttpResultsFilter
            {
                StringResult =
                    @"{
                          ""access_token"": ""token456"",
                          ""id_token"": ""eyJ0eXAiOiJKV1QiLCJhbGciOiJub25lIn0.eyJhdWQiOiIyZDRkMTFhMi1mODE0LTQ2YTctODkwYS0yNzRhNzJhNzMwOWUiLCJpc3MiOiJodHRwczovL3N0cy53aW5kb3dzLm5ldC83ZmU4MTQ0Ny1kYTU3LTQzODUtYmVjYi02ZGU1N2YyMTQ3N2UvIiwiaWF0IjoxMzg4NDQwODYzLCJuYmYiOjEzODg0NDA4NjMsImV4cCI6MTM4ODQ0NDc2MywidmVyIjoiMS4wIiwidGlkIjoiN2ZlODE0NDctZGE1Ny00Mzg1LWJlY2ItNmRlNTdmMjE0NzdlIiwib2lkIjoiNjgzODlhZTItNjJmYS00YjE4LTkxZmUtNTNkZDEwOWQ3NGY1IiwidXBuIjoiZnJhbmttQGNvbnRvc28uY29tIiwidW5pcXVlX25hbWUiOiJmcmFua21AY29udG9zby5jb20iLCJzdWIiOiJKV3ZZZENXUGhobHBTMVpzZjd5WVV4U2hVd3RVbTV5elBtd18talgzZkhZIiwiZmFtaWx5X25hbWUiOiJNaWxsZXIiLCJnaXZlbl9uYW1lIjoiRnJhbmsifQ.""
                        }"
            })
            {
                var session = new AuthUserSession();

                try
                {
                    subject.Authenticate(mockAuthService.Object, session, auth);
                }
                catch (UnauthorizedAccessException)
                {
                }

                Assert.False(session.IsAuthenticated);
            }
        }

        [Fact, Order(5)]
        public void ShouldSaveOAuth2StateValue()
        {
            var appHost = TestServer.Current;
            var subject = new AzureAuthenticationProvider(new TestAzureGraphService());
            var auth = new Authenticate
            {
                UserName = "some.user@foodomain.com"
            };
            var session = new AuthUserSession();

            var response = subject.Authenticate(MockAuthService(appHost: appHost).Object, session, auth);

            var result = (IHttpResult)response;
            var codeRequest = new Uri(result.Headers["Location"]);
            var query = PclExportClient.Instance.ParseQueryString(codeRequest.Query);
            var state = query["state"];
            Assert.Equal(session.State, state);
        }


        [Fact, Order(5)]
        public void ShouldAbortIfStateValuesDoNotMatch()
        {
           // var appHost = TestServer.Current;
            var subject = new AzureAuthenticationProvider(new TestAzureGraphService());
            var auth = new Authenticate
            {
                UserName = "some.user@foodomain.com"
            };

            subject.CallbackUrl = "http://localhost/myapp/";
            var request = new MockHttpRequest("myapp", "GET", "text", "/myapp", new NameValueCollection
                {
                    {"code", "code123"},
                    {"session_state", "dontcare"},
                    {"state", "state123"}
                }, Stream.Null, new NameValueCollection());
            var mockAuthService = MockAuthService(request);
            using (new HttpResultsFilter
            {
                StringResultFn = (tokenRequest, s) => @"{
                          ""access_token"": ""fake token"",
                          ""id_token"": ""eyJ0eXAiOiJKV1QiLCJhbGciOiJub25lIn0.eyJhdWQiOiIyZDRkMTFhMi1mODE0LTQ2YTctODkwYS0yNzRhNzJhNzMwOWUiLCJpc3MiOiJodHRwczovL3N0cy53aW5kb3dzLm5ldC83ZmU4MTQ0Ny1kYTU3LTQzODUtYmVjYi02ZGU1N2YyMTQ3N2UvIiwiaWF0IjoxMzg4NDQwODYzLCJuYmYiOjEzODg0NDA4NjMsImV4cCI6MTM4ODQ0NDc2MywidmVyIjoiMS4wIiwidGlkIjoiN2ZlODE0NDctZGE1Ny00Mzg1LWJlY2ItNmRlNTdmMjE0NzdlIiwib2lkIjoiNjgzODlhZTItNjJmYS00YjE4LTkxZmUtNTNkZDEwOWQ3NGY1IiwidXBuIjoiZnJhbmttQGNvbnRvc28uY29tIiwidW5pcXVlX25hbWUiOiJmcmFua21AY29udG9zby5jb20iLCJzdWIiOiJKV3ZZZENXUGhobHBTMVpzZjd5WVV4U2hVd3RVbTV5elBtd18talgzZkhZIiwiZmFtaWx5X25hbWUiOiJNaWxsZXIiLCJnaXZlbl9uYW1lIjoiRnJhbmsifQ.""
                        }"
            })
            {
                var session = new AuthUserSession
                {
                    State = "state133" // Not the same as the state in the request above
                };

                try
                {
                    subject.Authenticate(mockAuthService.Object, session, auth);
                }
                catch (UnauthorizedAccessException)
                {
                }

                Assert.False(session.IsAuthenticated);
            }
        }

        internal Mock<IServiceBase> MockAuthService(MockHttpRequest request = null, ServiceStackHost appHost = null)
        {
            request = request ?? new MockHttpRequest();
            var mockAuthService = new Mock<IServiceBase>();
            mockAuthService.SetupGet(s => s.Request).Returns(request);
            if (appHost != null)
                mockAuthService.Setup(s => s.TryResolve<IApplicationRegistryService>()).Returns(
                    appHost.Resolve<IApplicationRegistryService>());

            return mockAuthService;
        }

        #endregion
    }
}