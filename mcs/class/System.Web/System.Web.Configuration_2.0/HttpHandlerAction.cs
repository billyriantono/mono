//
// System.Web.Configuration.HttpHandlerAction
//
// Authors:
//	Chris Toshok (toshok@ximian.com)
//
// (C) 2005 Novell, Inc (http://www.novell.com)
//

//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

#if NET_2_0

using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Util;

namespace System.Web.Configuration
{
	public sealed class HttpHandlerAction: ConfigurationElement
	{
		static ConfigurationPropertyCollection _properties;
		static ConfigurationProperty pathProp;
		static ConfigurationProperty typeProp;
		static ConfigurationProperty validateProp;
		static ConfigurationProperty verbProp;

		static HttpHandlerAction ()
		{
			pathProp = new ConfigurationProperty ("path", typeof (string), null,
							      TypeDescriptor.GetConverter (typeof (string)),
							      PropertyHelper.NonEmptyStringValidator,
							      ConfigurationPropertyOptions.IsRequired | ConfigurationPropertyOptions.IsKey);
			typeProp = new ConfigurationProperty ("type", typeof (string), null,
							      TypeDescriptor.GetConverter (typeof (string)),
							      PropertyHelper.NonEmptyStringValidator,
							      ConfigurationPropertyOptions.IsRequired);
			validateProp = new ConfigurationProperty ("validate", typeof (bool), true);
			verbProp = new ConfigurationProperty ("verb", typeof (string), null,
							      TypeDescriptor.GetConverter (typeof (string)),
							      PropertyHelper.NonEmptyStringValidator,
							      ConfigurationPropertyOptions.IsRequired | ConfigurationPropertyOptions.IsKey);

			_properties = new ConfigurationPropertyCollection ();
			_properties.Add (pathProp);
			_properties.Add (typeProp);
			_properties.Add (validateProp);
			_properties.Add (verbProp);
		}

		internal HttpHandlerAction ()
		{ }

		public HttpHandlerAction (string path, string type, string verb)
			: this (path, type, verb, true)
		{ }

		public HttpHandlerAction (string path, string type, string verb, bool validate)
		{
			Path = path;
			Type = type;
			Verb = verb;
			Validate = validate;
		}

		[ConfigurationProperty ("path", Options = ConfigurationPropertyOptions.IsRequired | ConfigurationPropertyOptions.IsKey)]
		// LAMESPEC: MS lists no validator here but provides one in Properties.
		public string Path {
			get { return (string) base[pathProp]; }
			set { base[pathProp] = value; }
		}

		[ConfigurationProperty ("type", Options = ConfigurationPropertyOptions.IsRequired)]
		// LAMESPEC: MS lists no validator here but provides one in Properties.
		public string Type {
			get { return (string) base[typeProp]; }
			set { base[typeProp] = value; }
		}

		[ConfigurationProperty ("validate", DefaultValue = true)]
		public bool Validate {
			get { return (bool) base[validateProp]; }
			set { base[validateProp] = value; }
		}

		[ConfigurationProperty ("verb", Options = ConfigurationPropertyOptions.IsRequired | ConfigurationPropertyOptions.IsKey)]
		// LAMESPEC: MS lists no validator here but provides one in Properties.
		public string Verb {
			get { return (string) base[verbProp]; }
			set { base[verbProp] = value; }
		}

		protected override ConfigurationPropertyCollection Properties {
			get { return _properties; }
		}

#region CompatabilityCode
		object instance;
		Type type;

		string cached_verb = null;
		string[] cached_verbs;

		string cached_path = null;
		FileMatchingInfo[] cached_files;

		FileMatchingInfo[] SplitPaths ()
		{
			string [] paths = Path.Split (',');
			cached_files = new FileMatchingInfo [paths.Length];

			int i = 0;
			foreach (string s in paths)
				cached_files [i++] = new FileMatchingInfo (s);

			return cached_files;
		}

		string[] SplitVerbs ()
		{
			if (Verb == "*")
				cached_verbs = null;
			else
				cached_verbs = Verb.Split (',');

			return cached_verbs;
		}

		internal string[] Verbs {
			get {
				if (cached_verb != Verb) {
					cached_verbs = SplitVerbs();
					cached_verb = Verb;
				}

				return cached_verbs;
			}
		}

		FileMatchingInfo[] Paths {
			get {
				if (cached_path != Path) {
					cached_files = SplitPaths ();
					cached_path = Path;
				}

				return cached_files;
			}
		}

		//
		// Loads the a type by name and verifies that it implements
		// IHttpHandler or IHttpHandlerFactory
		//
		internal static Type LoadType (string type_name)
		{
			Type t = null;
			
			t = HttpApplication.LoadType (type_name, false);

			if (t == null)
				throw new HttpException (String.Format ("Failed to load httpHandler type `{0}'", type_name));

			if (typeof (IHttpHandler).IsAssignableFrom (t) ||
			    typeof (IHttpHandlerFactory).IsAssignableFrom (t))
				return t;
			
			throw new HttpException (String.Format ("Type {0} does not implement IHttpHandler or IHttpHandlerFactory", type_name));
		}

		internal bool PathMatches (string p)
		{
			int slash = p.LastIndexOf ('/');
			string orig = p;
			if (slash != -1)
				p = p.Substring (slash);

			for (int j = Paths.Length; j > 0; ){
				j--;
				FileMatchingInfo fm = Paths [j];

				if (fm.MatchExact != null)
					return fm.MatchExact.Length == p.Length && StrUtils.EndsWith (p, fm.MatchExact);
					
				if (fm.EndsWith != null)
					return StrUtils.EndsWith (p, fm.EndsWith);

				if (fm.MatchExpr == "*")
					return true;

				/* convert to regexp */
				return fm.RegExp.IsMatch (orig);
			}
			return false;
		}

		// Loads the handler, possibly delay-loaded.
		internal object GetHandlerInstance ()
		{
			IHttpHandler ihh = instance as IHttpHandler;
			
			if (instance == null || (ihh != null && !ihh.IsReusable)){
				if (type == null)
					type = LoadType (Type);

				instance = Activator.CreateInstance (type);
			} 
			
			return instance;
		}

		class FileMatchingInfo {
			public string MatchExact;
			public string MatchExpr;

			// If set, we can fast-path the patch with string.EndsWith (FMI.EndsWith)
			public string EndsWith;
			public Regex RegExp;
		
			public FileMatchingInfo (string s)
			{
				MatchExpr = s;

				if (s[0] == '*' && (s.IndexOf ('*', 1) == -1))
					EndsWith = s.Substring (1);

				if (s.IndexOf ('*') == -1)
					MatchExact = "/" + s;

				if (MatchExpr != "*") {
					string expr = MatchExpr.Replace(".", "\\.").Replace("?", "\\?").Replace("*", ".*");
					if (expr.Length > 0 && expr [0] =='/')
						expr = expr.Substring (1);

					expr += "\\z";
					RegExp = new Regex (expr);
				}
			}
		}
#endregion

	}

}

#endif
