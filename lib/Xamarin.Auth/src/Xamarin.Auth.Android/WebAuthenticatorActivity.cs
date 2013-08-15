//
//  Copyright 2012-2013, Xamarin Inc.
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
//
using System;
using Android.App;
using Android.Webkit;
using Android.OS;
using System.Threading.Tasks;
using Xamarin.Utilities.Android;
using System.Timers;
using System.Collections.Generic;

using Java.Interop;

namespace Xamarin.Auth
{
	[Activity (Label = "Web Authenticator")]
#if XAMARIN_AUTH_INTERNAL
	internal class WebAuthenticatorActivity : Activity
#else
	public class WebAuthenticatorActivity : Activity
#endif
	{
		WebView webView;

		internal class State : Java.Lang.Object
		{
			public WebAuthenticator Authenticator;
		}
		internal static readonly ActivityStateRepository<State> StateRepo = new ActivityStateRepository<State> ();

		State state;

		public class JavascriptInterceptor : Java.Lang.Object
		{
			private WebAuthenticatorActivity _activity;

			public JavascriptInterceptor (WebAuthenticatorActivity activity)
			{
				_activity = activity;
			}

			[Export]
			public void OnReceivedSamlResponse (string base64SamlResponse)
			{
				Console.WriteLine ("SAMLResponse={0}", base64SamlResponse);
				_activity.OnSamlResponseReceived2 (base64SamlResponse);
			}
		}

		protected override void OnCreate (Bundle savedInstanceState)
		{
			base.OnCreate (savedInstanceState);

			//
			// Load the state either from a configuration change or from the intent.
			//
			state = LastNonConfigurationInstance as State;
			if (state == null && Intent.HasExtra ("StateKey")) {
				var stateKey = Intent.GetStringExtra ("StateKey");
				state = StateRepo.Remove (stateKey);
			}
			if (state == null) {
				Finish ();
				return;
			}

			Title = state.Authenticator.Title;

			//
			// Watch for completion
			//
			state.Authenticator.Completed += (s, e) => {
				SetResult (e.IsAuthenticated ? Result.Ok : Result.Canceled);
				Finish ();
			};
			state.Authenticator.Error += (s, e) => {
				if (e.Exception != null) {
					this.ShowError ("Authentication Error", e.Exception);
				}
				else {
					this.ShowError ("Authentication Error", e.Message);
				}
				BeginLoadingInitialUrl ();
			};

			//
			// Build the UI
			//
			webView = new WebView (this) {
				Id = 42,
			};

			JavascriptInterceptor jsInterceptor = new JavascriptInterceptor (this);
			webView.AddJavascriptInterface (jsInterceptor, "jsInterceptor");

			webView.Settings.JavaScriptEnabled = true;
			webView.SetWebViewClient (new Client (this));
			webView.SetWebChromeClient (new ChromeClient ());

			SetContentView (webView);

			//
			// Restore the UI state or start over
			//
			if (savedInstanceState != null) {
				webView.RestoreState (savedInstanceState);
			}
			else {
				if (Intent.GetBooleanExtra ("ClearCookies", true))
					WebAuthenticator.ClearCookies();

				BeginLoadingInitialUrl ();
			}
		}

		void BeginLoadingInitialUrl ()
		{
			state.Authenticator.GetInitialUrlAsync ().ContinueWith (t => {
				if (t.IsFaulted) {
					this.ShowError ("Authentication Error", t.Exception);
				}
				else {
					webView.LoadUrl (t.Result.AbsoluteUri);
				}
			}, TaskScheduler.FromCurrentSynchronizationContext ());
		}

		public override void OnBackPressed ()
		{
			state.Authenticator.OnCancelled ();
		}

		public override Java.Lang.Object OnRetainNonConfigurationInstance ()
		{
			return state;
		}

		protected override void OnSaveInstanceState (Bundle outState)
		{
			base.OnSaveInstanceState (outState);
			webView.SaveState (outState);
		}

		public void OnSamlResponseReceived2 (string samlResponse)
		{
			this.RunOnUiThread (delegate {
				Dictionary<string,string> formParams = new Dictionary<string,string> ();
				formParams.Add ("SAMLResponse", samlResponse);

				this.state.Authenticator.OnPageLoading (new Uri (webView.Url), formParams);
				this.EndProgress ();
				this.webView.StopLoading ();
			});			
		}

		void BeginProgress (string message)
		{
			webView.Enabled = false;
		}

		void EndProgress ()
		{
			webView.Enabled = true;
		}

		class ChromeClient : WebChromeClient
		{
			public override bool OnJsAlert (WebView view, string url, string message, JsResult result)
			{
				return base.OnJsAlert (view, url, message, result);
			}

			public override bool OnConsoleMessage (ConsoleMessage consoleMessage)
			{
				Console.WriteLine (consoleMessage.Message ());
				return base.OnConsoleMessage (consoleMessage);
			}
		}

		class Client : WebViewClient
		{
			WebAuthenticatorActivity activity;

			public Client (WebAuthenticatorActivity activity)
			{
				this.activity = activity;
			}

			public override bool ShouldOverrideUrlLoading (WebView view, string url)
			{
				return false;
			}

			public override void OnPageStarted (WebView view, string url, Android.Graphics.Bitmap favicon)
			{
				var uri = new Uri (url);
				view.LoadUrl ("javascript:var resp=document.getElementsByName('SAMLResponse'); if(resp[0] && resp[0].value) { window.jsInterceptor.OnReceivedSamlResponse(resp[0].value); }");
//				view.LoadUrl ("javascript:var resp=document.getElementsByTagName('input'); if(resp[0]){alert(resp[0].value);} if(resp[0] && resp[0].value) { window.jsInterceptor.OnReceivedSamlResponse(resp[0].value); }");
//				view.LoadUrl ("javascript:alert(document.forms[0].value);");

//				activity.state.Authenticator.OnPageLoading (uri, formParams);
				activity.BeginProgress (uri.Authority);
			}

			public override void OnPageFinished (WebView view, string url)
			{
				var uri = new Uri (url);
//				activity.state.Authenticator.OnPageLoaded (uri);
				activity.EndProgress ();
			}
		}
	}
}
