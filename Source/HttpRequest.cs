//--------------------------------------
//          Kulestar Unity HTTP
//
//    Copyright © 2013 Kulestar Ltd
//          www.kulestar.com
//--------------------------------------

#if UNITY_IPHONE || UNITY_ANDROID || UNITY_WP8 || UNITY_BLACKBERRY
	#define MOBILE
#endif

#if UNITY_2_6 || UNITY_3_0 || UNITY_3_1 || UNITY_3_2 || UNITY_3_3 || UNITY_3_4 || UNITY_3_5
	#define PRE_UNITY4
#endif

#if PRE_UNITY4 || UNITY_4_0 || UNITY_4_1 || UNITY_4_2 || UNITY_4_3 || UNITY_4_4
	#define PRE_UNITY4_5
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using System.Net;
using System.IO;
using PowerUI;
using Dom;
using System.Net.Security;
#if !NETFX_CORE
using System.Security.Cryptography.X509Certificates;
#endif

namespace PowerUI.Http{
	
	public delegate void HttpEvent(HttpRequest source, string err);
	public delegate void HttpDataEvent(HttpRequest source, byte[] data, int count);
	
	/// <summary>
	/// Represents a single http request. Follows redirections.
	/// Generally don't use this directly; instead create either an XMLHttpRequest
	/// or e.g. a DataPackage.
	/// </summary>
	
	public partial class HttpRequest : IAbortable{
		
		/// <summary>The url that was requested.</summary>
		public Location location{
			get{
				return Package.location;
			}
		}
		
		/// <summary>The url that was requested.</summary>
		public string url{
			get{
				return location.absolute;
			}
		}
		
		/// <summary>Number of redirects.</summary>
		public int RedirectionCount;

		/// <summary>Timeout if there is one.</summary>
		public float Timeout_=float.MaxValue;
		/// <summary>How long this request has taken so far.</summary>
		public float Duration;
		/// <summary>Active requests are in a linked list. The http request that follows this one.</summary>
		public HttpRequest RequestAfter;
		/// <summary>Active requests are in a linked list. The http request that is before this one.</summary>
		public HttpRequest RequestBefore;
		/// <summary>The package this request originates from.</summary>
		public ContentPackage Package;
		/// <summary>The set of request headers. Pulled from the package.</summary>
		private Dictionary<string,string> RequestHeaders;
		/// <summary>Event called when bytes have been downloaded.</summary>
		public HttpDataEvent OnData;
		/// <summary>Event called when the request started.</summary>
		public HttpEvent OnStart;
		/// <summary>Event called when the request finished.</summary>
		public HttpEvent OnComplete;
		/// <summary>Event called when the headers are available.</summary>
		public HttpEvent OnHeaders;
		/// <summary>Event called when something went wrong.</summary>
		public HttpEvent OnError;
		/// <summary>Raw error message if available.</summary>
		private string _rawError;
		/// <summary>Stores the complete response if requested to.</summary>
		private MemoryStream CompleteResponse;
		/// <summary>Size of the download buffer.</summary>
		public const int BUFFER_SIZE = 4096;
		/// <summary>The download buffer.</summary>
		private byte[] buffer;
		/// <summary>The response status code.</summary>
		private int _statusCode = 0;
		private bool InQueue = false;
		/// <summary>Raw web request.</summary>
		public HttpWebRequest rawRequest;
		/// <summary>Raw web response.</summary>
		public HttpWebResponse rawResponse;
		/// <summary>Raw download stream.</summary>
		private Stream stream;
		/// <summary>Bytes downloaded so far.</summary>
		public int BytesDownloaded;
		/// <summary>Size of the response.</summary>
		public int ResponseSize = -1;
		/// <summary>True if the complete response should be saved, making things like .Text, .Image and .Bytes work.</summary>
		public bool saveCompleteResponse;
		/// <summary>Downloader for movie textures.</summary>
		private UnityEngine.Networking.DownloadHandlerMovieTexture movieTexDownloader;
		
		
		/// <summary>Creates a new http request using the given package.</summary>
		/// <param name="package">The package that will receive the updates.</param>
		/// <param name="onDone">A method to call with the result.</param>
		public HttpRequest(ContentPackage package){
			
			// Set package:
			Package=package;
			
			// Hook up as abortable:
			package.abortableObject=this;
			
		}
		
		/// <summary>Sends this request.
		/// Note that this does not block and is thread safe. Instead, OnRequestDone will be called when it's done.</summary>
		public void Send(){
			
			// Clear RDC:
			RedirectionCount=0;
			
			// Get the cookie jar:
			CookieJar jar=location.CookieJar;
			
			if(jar!=null){
				// We've got a cookie jar!
				
				// Set cookie string:
				Package.requestHeaders["cookie"]=jar.GetCookieHeader(location);
				
			}
			
			// Got auth?
			if(location.authorization!=null){
				
				// Get bytes:
				byte[] authBytes=System.Text.Encoding.UTF8.GetBytes(location.authorization);
				
				// Apply the auth header:
				Package.requestHeaders["authorization"]=System.Convert.ToBase64String(authBytes);
				
			}
			
			// Request now:
			RequestHeaders=Package.requestHeaders.ToSingleSet();
			
			BeginRequest(url, Package.request, Package.requestMethod, RequestHeaders, Package is ImagePackage, Package.ForceVideo);
			
			// Add to timeout-able queue:
			Web.Add(this);
			InQueue = true;
		}
		
		public void BeginRequest(string url, byte[] data, string method, Dictionary<string, string> headers, bool isVisualMedia, bool forceVideo) {
			if(buffer == null){
				buffer = new byte[BUFFER_SIZE];
			}
			
			if(saveCompleteResponse){
				CompleteResponse = new MemoryStream();
			}
			
			#if !NETFX_CORE
			ServicePointManager.ServerCertificateValidationCallback = CertValidation;
			#endif
			
			// Create the request:
			rawRequest = (HttpWebRequest)WebRequest.Create(url);
			
			rawRequest.Method = method == null ? ((data == null || data.Length == 0) ? "GET" : "POST") : method;
			
			if(data != null){
				rawRequest.ContentLength = data.Length;
			}
			
			// Add the headers:
			if(headers != null) {
				foreach(var kvp in headers){
					var lowerKey = kvp.Key.ToLower();
					if(lowerKey == "content-type") {
						rawRequest.ContentType = kvp.Value;
					} else if(lowerKey == "content-length"){
						rawRequest.ContentLength = int.Parse(kvp.Value);
					}else if(lowerKey == "user-agent"){
						rawRequest.UserAgent = kvp.Value;
					}else if(lowerKey == "if-modified-since"){
						rawRequest.IfModifiedSince = DateTime.Parse(kvp.Value);
					}else if(lowerKey == "transfer-encoding"){
						rawRequest.TransferEncoding = kvp.Value;
					}else if(lowerKey == "connection"){
						rawRequest.Connection = kvp.Value;
					}else if(lowerKey == "expect"){
						rawRequest.Expect = kvp.Value;
					}else{
						rawRequest.Headers[kvp.Key] = kvp.Value;
					}
				}
			}
			
			// Get the request stream:
			if(data != null && data.Length != 0) {
				rawRequest.BeginGetRequestStream(new AsyncCallback(areq => {
					
					// Sent the headers - write the request payload.
					var reqStream = rawRequest.EndGetRequestStream(areq);
					reqStream.Write(data, 0, data.Length);
					reqStream.Close();
					
					SendNow(isVisualMedia, forceVideo);
					
				}), this);
			} else {
				SendNow(isVisualMedia, forceVideo);
			}
		}
		
		#if !NETFX_CORE
		internal static bool CertValidation(System.Object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) {
			 bool isOk = true;
			 // If there are errors in the certificate chain, look at each error to determine the cause.
			 if (sslPolicyErrors != SslPolicyErrors.None) {
				 for(int i=0; i<chain.ChainStatus.Length; i++) {
					 if(chain.ChainStatus[i].Status != X509ChainStatusFlags.RevocationStatusUnknown) {
						 chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
						 chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
						 chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0);
						 chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
						 bool chainIsValid = chain.Build((X509Certificate2)certificate);
						 if(!chainIsValid) {
							 isOk = false;
						 }
					 }
				 }
			 }
			 return isOk;
		 }
		 #endif
		 
		private void SendNow(bool isVisualMedia, bool forceVideo){
			
			if(OnStart!=null){
				OnStart(this, null);
			}
			
			// Send it now:
			rawRequest.BeginGetResponse(new AsyncCallback(ares =>
			{
				try
				{
					// Headers received
					rawResponse = (HttpWebResponse)rawRequest.EndGetResponse(ares);
					ResponseSize = (int)rawResponse.ContentLength;
					
					if(OnHeaders!=null){
						OnHeaders(this, null);
					}
					
					if(isVisualMedia && (forceVideo || rawResponse.ContentType.Trim().ToLower().StartsWith("video/"))){
						movieTexDownloader = new UnityEngine.Networking.DownloadHandlerMovieTexture();
						MovieTextureHacks.ReceiveContentLength.Invoke(movieTexDownloader, new object[]{ResponseSize});
					}
					
					// Received headers:
					Package.responseHeaders.Apply(StatusLine, ResponseHeaders, ResponseSize);
					bool redirect=Package.ReceivedHeaders();
					
					if(redirect){
						
						// Redirection. We'll most likely be making another request, unless we've redirected too many times:
						RedirectionCount++;
						
						if(RedirectionCount>=20){
							
							// Failed. Too many redirects.
							Package.statusCode=ErrorHandlers.TooManyRedirects;
							RemoveFromQueue();
							return;
							
						}else{
							
							// Redirect now (note that ready state was unchanged - redirects are supposed to be silent):
							Duration=0f;
							
							// Get the location:
							string redirectedTo=Package.responseHeaders["location"];
							
							// Set redir to:
							Package.redirectedTo=new Location(redirectedTo,location);
							
							// Get absolute:
							redirectedTo=Package.redirectedTo.absoluteNoHash;
							
							if(string.IsNullOrEmpty(redirectedTo) || redirectedTo.Trim()==""){
								
								// Pop it from the update queue:
								RemoveFromQueue();
								
								// Failed!
								Package.Failed(500);
								
							}else{
								
								if(Package.statusCode==307 || Package.statusCode==308){
									
									// Resend as-is to the new URI:
									BeginRequest(redirectedTo,Package.request, Package.requestMethod,RequestHeaders, Package is ImagePackage, Package.ForceVideo);
									
								}else{
									
									// GET request to the new URI:
									BeginRequest(redirectedTo,null, Package.requestMethod,RequestHeaders, Package is ImagePackage, Package.ForceVideo);
									
								}
								
								return;
								
							}
							
						}
						
					}
					
					stream = rawResponse.GetResponseStream();
					ReadNext();
				} catch(WebException ex) {
					_rawError = ex.ToString();
					_statusCode = (int)ex.Status;
					if(OnError != null) {
						OnError(this, _rawError);
					}
					// Read the status code:
					Package.Failed(_statusCode);
				} catch (Exception ex) {
					_rawError = ex.ToString();
					UnityEngine.Debug.Log("Http error: " + _rawError);
					if(OnError != null) {
						OnError(this, _rawError);
					}
					Package.Failed(500);
				}
				
			}), this);
		}
		
		/// <summary>Timeout in ms. Default is 0.</summary>
		public float timeout{
			get{
				return Timeout_;
			}
			set{
				Timeout_=value;
			}
		}
		
		/// <summary>Aborts this request (IAbortable interface).</summary>
		public void abort(){
			rawRequest.Abort();
			RemoveFromQueue();
		}
		
		/// <summary>Removes this request from the active linked list. It won't be updated anymore.</summary>
		public void Remove(){
			abort();
		}
		
		private void RemoveFromQueue(){
			if(!InQueue){
				return;
			}
			InQueue = false;
			if(RequestBefore==null){
				Web.FirstRequest=RequestAfter;
			}else{
				RequestBefore.RequestAfter=RequestAfter;
			}
			
			if(RequestAfter==null){
				Web.LastRequest=RequestBefore;
			}else{
				RequestAfter.RequestBefore=RequestBefore;
			}
		}
		
		/// <summary>True if the request failed.</summary>
		public bool Errored{
			get{
				if(_rawError != null){
					return true;
				}
				int sc = StatusCode;
				return (sc >= 400 || sc < 200);
			}
		}
		
		/// <summary>True if the request was successful.</summary>
		public bool Ok{
			get{
				int sc = StatusCode;
				return (sc < 400 && sc >= 200);
			}
		}
		
		/// <summary>Error message.</summary>
		public string Error{
			get{
				if(_rawError != null){
					return _rawError;
				}
				return "The response was a " + StatusCode;
			}
		}
		
		/// <summary>The response content type.</summary>
		public string ContentType{
			get{
				if(rawResponse == null){
					return null;
				}
				return rawResponse.ContentType;
			}
		}
		
		/// <summary>The response headers.</summary>
		public WebHeaderCollection ResponseHeaders{
			get{
				if(rawResponse == null){
					return null;
				}
				return rawResponse.Headers;
			}
		}
		
		/// <summary>The response as text. Null if there was an error.</summary>
		public string Text{
			get{
				byte[] data = Bytes;
				if(data == null){
					return null;
				}
				return System.Text.Encoding.UTF8.GetString(data);
			}
		}
		
		/// <summary>The raw bytes of the response. Null if there was an error.</summary>
		public byte[] Bytes{
			get{
				if(Errored){
					return null;
				}
				if(CompleteResponse == null){
					throw new Exception("Only available if you cache the complete response (via saveCompleteResponse value set to true).");
				}
				return CompleteResponse.ToArray();
			}
		}
		
		/// <summary>The response as an image. Null if there was an error.</summary>
		public Texture2D Image{
			get{
				var fullResponse = Bytes;
				if(fullResponse == null){
					return null;
				}
				var tex = new Texture2D(0,0);
				tex.LoadImage(fullResponse);
				return tex;
			}
		}
		
		public string StatusLine{
			get{
				if(rawResponse == null){
					return null;
				}
				return ProtocolVersion + " " + StatusCode + " " + StatusText;
			}
		}
		
		public string ProtocolVersion{
			get{
				if(rawResponse == null){
					return null;
				}
				return "HTTP/" + rawResponse.ProtocolVersion;
			}
		}
		
		public int StatusCode{
			get{
				if(rawResponse == null){
					return _statusCode;
				}
				return (int)rawResponse.StatusCode;
			}
		}
		
		public string StatusText{
			get{
				if(rawResponse == null){
					return null;
				}
				return rawResponse.StatusDescription;
			}
		}
		
		#if !MOBILE && !UNITY_WEBGL
		public MovieTexture Video{
			get{
				if(movieTexDownloader != null){
					return movieTexDownloader.movieTexture;
				}
				return null;
			}
		}
		#endif
		
		public float Progress{
			get{
				return (float)BytesDownloaded / (float)ResponseSize;
			}
		}
		
		private void ReadNext() {
			stream.BeginRead(buffer, 0, BUFFER_SIZE, ares =>
			{
				try{
					int count = stream.EndRead(ares);
					if (count > 0)
					{
						BytesDownloaded += count;
						
						if(OnData != null){
							OnData(this, buffer, count);
						}
						
						if(CompleteResponse != null){
							CompleteResponse.Write(buffer, 0, count);
						}
						
						if(movieTexDownloader != null){
							MovieTextureHacks.ReceiveData.Invoke(movieTexDownloader, new object[]{buffer, count});
						}
						
						Package.ReceivedData(buffer,0,count);
						
						ReadNext();
					}
					else
					{
						stream.Close();
						RemoveFromQueue();
						
						if(movieTexDownloader!= null){
							MovieTextureHacks.CompleteContent.Invoke(movieTexDownloader, new object[]{});
						}
						if(OnComplete != null){
							OnComplete(this, null);
						}
						
						// Harmless receiving 0 indicates stream completed when content length is unknown:
						Package.ReceivedData(buffer,0,0);
						
					}
				}catch(Exception ex){
					if(OnError!=null){
						_rawError = ex.ToString();
						OnError(this, _rawError);
					}
				}
			}, this);
		}
		
		public void Update(float deltaTime){
			
			// Check for timeout:
			Duration+=deltaTime;
			
			if(Duration>=Timeout_){
				
				// Timeout!
				Package.TimedOut();
				
				// Done:
				abort();
				return;
				
			}
			
		}
		
	}
		
	#if !MOBILE && !UNITY_WEBGL
	public class MovieTextureHacks{
		
		/// <summary>HACK! Workaround for accessing ReceiveData directly.</summary>
		private static MethodInfo _receiveDataMethod;
		
		/// <summary>The response header string.</summary>
		public static MethodInfo ReceiveData{
			get{
				
				if(_receiveDataMethod==null){
					
					_receiveDataMethod = typeof(UnityEngine.Networking.DownloadHandlerMovieTexture).GetMethod(
						"ReceiveData",
						BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance
					);
					
				}
				
				return _receiveDataMethod;
			}
		}
		
		/// <summary>HACK! Workaround for accessing ReceiveContentLength directly.</summary>
		private static MethodInfo _receiveContentLength;
		
		/// <summary>The response header string.</summary>
		public static MethodInfo ReceiveContentLength{
			get{
				
				if(_receiveContentLength==null){
					
					_receiveContentLength = typeof(UnityEngine.Networking.DownloadHandlerMovieTexture).GetMethod(
						"ReceiveContentLength",
						BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance
					);
					
				}
				
				return _receiveContentLength;
			}
		}
		
		/// <summary>HACK! Workaround for accessing CompleteContent directly.</summary>
		private static MethodInfo _completeContent;
		
		/// <summary>The response header string.</summary>
		public static MethodInfo CompleteContent{
			get{
				
				if(_completeContent==null){
					
					_completeContent = typeof(UnityEngine.Networking.DownloadHandlerMovieTexture).GetMethod(
						"CompleteContent",
						BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance
					);
					
				}
				
				return _completeContent;
			}
		}
		
		/// <summary>HACK! Workaround for accessing GetProgress directly.</summary>
		private static MethodInfo _getProgress;
		
		/// <summary>The response header string.</summary>
		public static MethodInfo GetProgress{
			get{
				
				if(_getProgress==null){
					
					_getProgress = typeof(UnityEngine.Networking.DownloadHandlerMovieTexture).GetMethod(
						"GetProgress",
						BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance
					);
					
				}
				
				return _getProgress;
			}
		}
		
	}
	#endif
	
}