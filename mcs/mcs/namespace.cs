//
// namespace.cs: Tracks namespaces
//
// Author:
//   Miguel de Icaza (miguel@ximian.com)
//
// (C) 2001 Ximian, Inc.
//
using System;
using System.Collections;
using Mono.Languages;

namespace Mono.CSharp {

	/// <summary>
	///   Keeps track of the namespaces defined in the C# code.
	/// </summary>
	public class Namespace {
		static ArrayList all_namespaces = new ArrayList ();
		
		Namespace parent;
		string name, fullname;
		ArrayList entries;
		Hashtable namespaces;

		/// <summary>
		///   Constructor Takes the current namespace and the
		///   name.  This is bootstrapped with parent == null
		///   and name = ""
		/// </summary>
		public Namespace (Namespace parent, string name)
		{
			this.name = name;
			this.parent = parent;

			string pname = parent != null ? parent.Name : "";
				
			if (pname == "")
				fullname = name;
			else
				fullname = parent.Name + "." + name;

			entries = new ArrayList ();
			namespaces = new Hashtable ();

			all_namespaces.Add (this);
		}

		public static Namespace Root = new Namespace (null, "");

		public Namespace GetNamespace (string name, bool create)
		{
			int pos = name.IndexOf ('.');

			Namespace ns;
			string first;
			if (pos >= 0)
				first = name.Substring (0, pos);
			else
				first = name;

			ns = (Namespace) namespaces [first];
			if (ns == null) {
				if (!create)
					return null;

				ns = new Namespace (this, first);
				namespaces.Add (first, ns);
			}

			if (pos >= 0)
				ns = ns.GetNamespace (name.Substring (pos + 1), create);

			return ns;
		}

		public static Namespace LookupNamespace (string name, bool create)
		{
			return Root.GetNamespace (name, create);
		}

		public object Lookup (DeclSpace ds, string name)
		{
			Namespace ns = GetNamespace (name, false);
			if (ns != null)
				return ns;

			Type t = TypeManager.LookupType (DeclSpace.MakeFQN (fullname, name));
			if ((t == null) || ((ds != null) && !ds.CheckAccessLevel (t)))
				return null;

			return t;
		}

		public void AddNamespaceEntry (NamespaceEntry entry)
		{
			entries.Add (entry);
		}

		static public ArrayList UserDefinedNamespaces {
			get {
				return all_namespaces;
			}
		}

		/// <summary>
		///   The qualified name of the current namespace
		/// </summary>
		public string Name {
			get {
				return fullname;
			}
		}

		/// <summary>
		///   The parent of this namespace, used by the parser to "Pop"
		///   the current namespace declaration
		/// </summary>
		public Namespace Parent {
			get {
				return parent;
			}
		}

		public static void DefineNamespaces (SymbolWriter symwriter)
		{
			foreach (Namespace ns in all_namespaces) {
				foreach (NamespaceEntry entry in ns.entries)
					entry.DefineNamespace (symwriter);
			}
		}

		/// <summary>
		///   Used to validate that all the using clauses are correct
		///   after we are finished parsing all the files.  
		/// </summary>
		public static void VerifyUsing ()
		{
			foreach (Namespace ns in all_namespaces) {
				foreach (NamespaceEntry entry in ns.entries)
					entry.VerifyUsing ();
			}
		}

		public override string ToString ()
		{
			if (this == Root)
				return "Namespace (<root>)";
			else
				return String.Format ("Namespace ({0})", Name);
		}
	}

	public class NamespaceEntry
	{
		Namespace ns;
		NamespaceEntry parent, implicit_parent;
		SourceFile file;
		int symfile_id;
		Hashtable aliases;
		ArrayList using_clauses;
		public bool DeclarationFound = false;

		//
		// This class holds the location where a using definition is
		// done, and whether it has been used by the program or not.
		//
		// We use this to flag using clauses for namespaces that do not
		// exist.
		//
		public class UsingEntry {
			public readonly string Name;
			public readonly NamespaceEntry NamespaceEntry;
			public readonly Location Location;
			
			public UsingEntry (NamespaceEntry entry, string name, Location loc)
			{
				Name = name;
				NamespaceEntry = entry;
				Location = loc;
			}

			Namespace resolved_ns;

			public Namespace Resolve ()
			{
				if (resolved_ns != null)
					return resolved_ns;

				Namespace curr_ns = NamespaceEntry.NS;
				while ((curr_ns != null) && (resolved_ns == null)) {
					string full_name = DeclSpace.MakeFQN (curr_ns.Name, Name);
					resolved_ns = curr_ns.GetNamespace (Name, false);

					if (resolved_ns == null)
						curr_ns = curr_ns.Parent;
				}

				return resolved_ns;
			}
		}

		public class AliasEntry {
			public readonly string Name;
			public readonly string Alias;
			public readonly NamespaceEntry NamespaceEntry;
			public readonly Location Location;
			
			public AliasEntry (NamespaceEntry entry, string name, string alias, Location loc)
			{
				Name = name;
				Alias = alias;
				NamespaceEntry = entry;
				Location = loc;
			}

			object resolved;

			public object Resolve ()
			{
				if (resolved != null)
					return resolved;

				NamespaceEntry curr_ns = NamespaceEntry;
				while ((curr_ns != null) && (resolved == null)) {
					resolved = curr_ns.Lookup (null, Alias, Location);

					if (resolved == null)
						curr_ns = curr_ns.Parent;
				}

				return resolved;
			}
		}

		public NamespaceEntry (NamespaceEntry parent, SourceFile file, string name)
			: this (parent, file, name, false)
		{ }

		protected NamespaceEntry (NamespaceEntry parent, SourceFile file, string name, bool is_implicit)
		{
			this.parent = parent;
			this.file = file;
			this.IsImplicit = is_implicit;
			this.ID = ++next_id;

			if (!is_implicit && (parent != null))
				ns = parent.NS.GetNamespace (name, true);
			else if (name != null)
				ns = Namespace.LookupNamespace (name, true);
			else
				ns = Namespace.Root;
			ns.AddNamespaceEntry (this);

			if ((parent != null) && (parent.NS != ns.Parent))
				implicit_parent = new NamespaceEntry (parent, file, ns.Parent.Name, true);
			else
				implicit_parent = parent;
		}

		static int next_id = 0;
		public readonly int ID;
		public readonly bool IsImplicit;

		public string Name {
			get {
				return ns.Name;
			}
		}

		public Namespace NS {
			get {
				return ns;
			}
		}

		public NamespaceEntry Parent {
			get {
				return parent;
			}
		}

		public NamespaceEntry ImplicitParent {
			get {
				return implicit_parent;
			}
		}

		/// <summary>
		///   Records a new namespace for resolving name references
		/// </summary>
		public void Using (string ns, Location loc)
		{
			if (DeclarationFound){
				Report.Error (1529, loc, "A using clause must precede all other namespace elements");
				return;
			}

			if (ns == Name)
				return;
			
			if (using_clauses == null)
				using_clauses = new ArrayList ();

			foreach (UsingEntry old_entry in using_clauses){
				if (old_entry.Name == ns){
					Report.Warning (105, loc, "The using directive for '" + ns +
							"' appeared previously in this namespace");
					return;
				}
			}
			
			UsingEntry ue = new UsingEntry (this, ns, loc);
			using_clauses.Add (ue);
		}

		public void UsingAlias (string alias, string namespace_or_type, Location loc)
		{
			if (aliases == null)
				aliases = new Hashtable ();
			
			if (aliases.Contains (alias)){
				Report.Error (1537, loc, "The using alias `" + alias +
					      "' appeared previously in this namespace");
				return;
			}

			aliases [alias] = new AliasEntry (this, alias, namespace_or_type, loc);
		}

		protected AliasEntry GetAliasEntry (string alias)
		{
			AliasEntry entry = null;

			if (aliases != null)
				entry = (AliasEntry) aliases [alias];
			if (entry == null && Parent != null)
				entry = Parent.GetAliasEntry (alias);

			return entry;
		}

		public string LookupAlias (string alias)
		{
			AliasEntry entry = GetAliasEntry (alias);

			if (entry == null)
				return null;

			object resolved = entry.Resolve ();
			if (resolved == null)
				return null;
			else if (resolved is Namespace)
				return ((Namespace) resolved).Name;
			else
				return ((Type) resolved).FullName;
		}

		public object Lookup (DeclSpace ds, string name, Location loc)
		{
			object o;
			Namespace ns;

			//
			// If name is of the form `N.I', first lookup `N', then search a member `I' in it.
			//
			int pos = name.IndexOf ('.');
			if (pos >= 0) {
				string first = name.Substring (0, pos);
				string last = name.Substring (pos + 1);

				o = Lookup (ds, first, loc);
				if (o == null)
					return null;

				ns = o as Namespace;
				if (ns != null)
					return ns.Lookup (ds, last);

				Type nested = TypeManager.LookupType (DeclSpace.MakeFQN (((Type) o).Name, last));
				if ((nested == null) || ((ds != null) && !ds.CheckAccessLevel (nested)))
					return null;

				return nested;
			}

			//
			// Check whether it's a namespace.
			//
			o = NS.Lookup (ds, name);
			if (o != null)
				return o;

			//
			// Check aliases.
			//
			AliasEntry entry = GetAliasEntry (name);
			if (entry != null) {
				o = entry.Resolve ();
				if (o != null)
					return o;
			}

			//
			// Check using entries.
			//
			Type t = null, match = null;
			foreach (Namespace using_ns in GetUsingTable ()) {
				match = using_ns.Lookup (ds, name) as Type;
				if (match != null){
					if (t != null) {
						DeclSpace.Error_AmbiguousTypeReference (loc, name, t, match);
						return null;
					} else {
						t = match;
					}
				}
			}

			return t;
		}
		
		public Namespace[] GetUsingTable ()
		{
			ArrayList list = new ArrayList ();

			if (using_clauses == null)
				return new Namespace [0];

			foreach (UsingEntry ue in using_clauses) {
				Namespace using_ns = ue.Resolve ();
				if (using_ns == null)
					continue;

				list.Add (using_ns);
			}

			Namespace[] retval = new Namespace [list.Count];
			list.CopyTo (retval, 0);
			return retval;
		}

		public void DefineNamespace (SymbolWriter symwriter)
		{
			if (symfile_id != 0)
				return;
			if (parent != null)
				parent.DefineNamespace (symwriter);

			string[] using_list;
			if (using_clauses != null) {
				using_list = new string [using_clauses.Count];
				for (int i = 0; i < using_clauses.Count; i++)
					using_list [i] = ((UsingEntry) using_clauses [i]).Name;
			} else {
				using_list = new string [0];
			}

			int parent_id = parent != null ? parent.symfile_id : 0;
			symfile_id = symwriter.DefineNamespace (ns.Name, file, using_list, parent_id);
		}

		public int SymbolFileID {
			get {
				return symfile_id;
			}
		}

		static void Msgtry (string s)
		{
			Console.WriteLine ("    Try using -r:" + s);
		}

		protected void error246 (Location loc, string name)
		{
			if (TypeManager.LookupType (name) != null)
				Report.Error (138, loc, "The using keyword only lets you specify a namespace, " +
					      "`" + name + "' is a class not a namespace.");
			else {
				Report.Error (246, loc, "The namespace `" + name +
					      "' can not be found (missing assembly reference?)");

				switch (name){
				case "Gtk": case "GtkSharp":
					Msgtry ("gtk-sharp");
					break;

				case "Gdk": case "GdkSharp":
					Msgtry ("gdk-sharp");
					break;

				case "Glade": case "GladeSharp":
					Msgtry ("glade-sharp");
					break;
							
				case "System.Drawing":
					Msgtry ("System.Drawing");
					break;
							
				case "System.Web.Services":
					Msgtry ("System.Web.Services");
					break;

				case "System.Web":
					Msgtry ("System.Web");
					break;
							
				case "System.Data":
					Msgtry ("System.Data");
					break;

				case "System.Windows.Forms":
					Msgtry ("System.Windows.Forms");
					break;
				}
			}
		}

		/// <summary>
		///   Used to validate that all the using clauses are correct
		///   after we are finished parsing all the files.  
		/// </summary>
		public void VerifyUsing ()
		{
			if (using_clauses != null){
				foreach (UsingEntry ue in using_clauses){
					if (ue.Resolve () != null)
						continue;

					error246 (ue.Location, ue.Name);
				}
			}

			if (aliases != null){
				foreach (DictionaryEntry de in aliases){
					AliasEntry alias = (AliasEntry) de.Value;

					if (alias.Resolve () != null)
						continue;

					error246 (alias.Location, alias.Alias);
				}
			}
		}

		public override string ToString ()
		{
			if (NS == Namespace.Root)
				return "NamespaceEntry (<root>)";
			else
				return String.Format ("NamespaceEntry ({0},{1},{2})", Name, IsImplicit, ID);
		}
	}
}
