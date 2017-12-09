//--------------------------------------
//          Kulestar Unity HTTP
//
//    Copyright © 2013 Kulestar Ltd
//          www.kulestar.com
//--------------------------------------

using System;
using UnityEngine;

namespace PowerUI.Http{
	
	/// <summary>
	/// Performs Http requests independently.
	/// Note that you must call Update to check for timeouts.
	/// </summary>
	
	public static class Web{
		
		/// <summary>Active http requests are stored in a linked list. This is the tail of the list.</summary>
		public static HttpRequest LastRequest;
		/// <summary>Active http requests are stored in a linked list. This is the head of the list.</summary>
		public static HttpRequest FirstRequest;
		/// <summary>The amount of requests that are currently active.</summary>
		public static int CurrentActiveRequests;
		
		
		/// <summary>Clears all active requests.</summary>
		public static void Clear(){
			FirstRequest=LastRequest=null;
		}
		
		/// <summary>Encodes the given piece of text so it's suitable to go into a post or get string.</summary>
		/// <param name="text">The text to encode.</param>
		/// <returns>The url encoded text.</returns>
		public static string UrlEncode(string text){
			return System.Uri.EscapeDataString(text);
		}
		
		/// <summary>Decodes the given piece of text from a post or get string.</summary>
		/// <param name="text">The text to decode.</param>
		/// <returns>The url decoded text.</returns>
		public static string UrlDecode(string text){
			return System.Uri.UnescapeDataString(text);
		}
		
		/// <summary>Metered. Advances all the currently active http requests.</summary>
		public static void Update(float deltaTime){
			HttpRequest current=FirstRequest;
			
			while(current!=null){
				current.Update(deltaTime);
				current=current.RequestAfter;
			}
			
		}
		
		public static void Add(HttpRequest request){
			// Bump up the active count:
			CurrentActiveRequests++;
			
			// Add to main queue:
			if(FirstRequest==null){
				LastRequest=FirstRequest=request;
			}else{
				request.RequestBefore=LastRequest;
				LastRequest=LastRequest.RequestAfter=request;
			}
		}
		
	}
	
}