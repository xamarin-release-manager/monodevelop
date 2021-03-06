// 
// AddViewDialog.cs
//  
// Author:
//       Michael Hutchinson <mhutchinson@novell.com>
// 
// Copyright (c) 2009 Novell, Inc. (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using PP = System.IO.Path;

using MonoDevelop.AspNet.Gui;
using MonoDevelop.Ide;
using MonoDevelop.AspNet.Parser;
using MonoDevelop.Core;
using MonoDevelop.Components;
using ICSharpCode.NRefactory.TypeSystem;
using MonoDevelop.Ide.TypeSystem;

namespace MonoDevelop.AspNet.Mvc.Gui
{
	
	
	public partial class AddViewDialog : Gtk.Dialog
	{	
		AspMvcProject project;
		IList<string> loadedTemplateList;
		string oldMaster;
		Gtk.ListStore primaryPlaceholderStore = new Gtk.ListStore (typeof (String));
		System.CodeDom.Compiler.CodeDomProvider provider;
		DropDownBox dataClassCombo;
		TypeDataProvider classDataProvider;
		
		public AddViewDialog (AspMvcProject project)
		{
			this.project = project;
			this.Build ();
			
			dataClassCombo = new DropDownBox ();
			
			int w, h;
			Gtk.Icon.SizeLookup (Gtk.IconSize.Menu, out w, out h);
			dataClassCombo.DefaultIconHeight = Math.Max (h, 16);
			dataClassCombo.DefaultIconWidth = Math.Max (w, 16);
			dataClassAlignment.Add (dataClassCombo);
			dataClassAlignment.QueueResize ();
			dataClassCombo.ShowAll ();
			
			provider = project.LanguageBinding.GetCodeDomProvider ();
			
			ContentPlaceHolders = new List<string> ();
			string siteMaster = project.VirtualToLocalPath ("~/Views/Shared/Site.master", null);
			if (project.Files.GetFile (siteMaster) != null)
				masterEntry.Text = "~/Views/Shared/Site.master";
			
			loadedTemplateList = project.GetCodeTemplates ("AddView");
			bool foundEmptyTemplate = false;
			int templateIndex = 0;
			foreach (string file in loadedTemplateList) {
				string name = PP.GetFileNameWithoutExtension (file);
				templateCombo.AppendText (name);
				if (!foundEmptyTemplate){
					if (name == "Empty") {
						templateCombo.Active = templateIndex;
						foundEmptyTemplate = true;
					} else
						templateIndex++;
				}
			}
			
			if (!foundEmptyTemplate)
				throw new Exception ("The Empty.tt template is missing.");
			
			primaryPlaceholderCombo.Model = primaryPlaceholderStore;
			
			UpdateTypePanelSensitivity (null, null);
			UpdateMasterPanelSensitivity (null, null);
			Validate ();
		}
		
		protected virtual void Validate (object sender, EventArgs e)
		{
			Validate ();
		}
		
		void Validate ()
		{
			buttonOk.Sensitive = IsValid ();
		}
	
		protected virtual void UpdateMasterPanelSensitivity (object sender, EventArgs e)
		{
			bool canHaveMaster = !IsPartialView;
			masterCheck.Sensitive = canHaveMaster;
			masterPanel.Sensitive = canHaveMaster && HasMaster;
			MasterChanged (null, null);
			Validate ();
		}
		
		protected virtual void UpdateTypePanelSensitivity (object sender, EventArgs e)
		{
			//FIXME: need to fix the class list widget
			bool enabled = typePanel.Sensitive = false; // stronglyTypedCheck.Active;
			
			if (enabled && classDataProvider == null) {
				dataClassCombo.DataProvider = classDataProvider = new TypeDataProvider (project);
				if (classDataProvider.List.Count > 0)
					dataClassCombo.SetItem (0);
			}
			
			Validate ();
		}
		
		public override void Dispose ()
		{
			Destroy ();
			base.Dispose ();
		}
		
		public bool IsValid ()
		{
			if (!IsValidIdentifier (ViewName))
				return false;
			
			if (!IsPartialView && HasMaster) {
				if (String.IsNullOrEmpty (MasterFile) || !System.IO.File.Exists (project.VirtualToLocalPath (oldMaster, null)))
				return false;
				//PrimaryPlaceHolder can be empty
			}
			
			if (IsStronglyTyped && (ViewDataType == null))
			    return false;
			
			return true;
		}
		
		bool IsValidIdentifier (string identifier)
		{
			return !String.IsNullOrEmpty (identifier) && provider.IsValidIdentifier (identifier);
		}
	
		protected virtual void ShowMasterSelectionDialog (object sender, System.EventArgs e)
		{
			var dialog = new MonoDevelop.Ide.Projects.ProjectFileSelectorDialog (project, null, "*.master") {
				Title = MonoDevelop.Core.GettextCatalog.GetString ("Select a Master Page..."),
				TransientFor = this,
			};
			try {
				if (MessageService.RunCustomDialog (dialog) == (int) Gtk.ResponseType.Ok)
					masterEntry.Text = project.LocalToVirtualPath (dialog.SelectedFile.FilePath);
			} finally {
				dialog.Destroy ();
			}
		}
		
		protected virtual void MasterChanged (object sender, EventArgs e)
		{
			if (IsPartialView || !HasMaster)
				return;
			
			if (masterEntry.Text == oldMaster)
				return;
			oldMaster = masterEntry.Text;
			
			primaryPlaceholderStore.Clear ();
			ContentPlaceHolders.Clear ();
			
			string realPath = project.VirtualToLocalPath (oldMaster, null);
			if (!File.Exists (realPath))
				return;
			
			var pd = TypeSystemService.ParseFile (project, realPath)
				as AspNetParsedDocument;
			
			if (pd != null) {
				try {
					var visitor = new ContentPlaceHolderVisitor ();
					pd.RootNode.AcceptVisit (visitor);
					ContentPlaceHolders.AddRange (visitor.PlaceHolders);
					
					for (int i = 0; i < ContentPlaceHolders.Count; i++) {
						string placeholder = ContentPlaceHolders[i];
						primaryPlaceholderStore.AppendValues (placeholder);
						
						if (placeholder.Contains ("main") || placeholder.Contains ("Main") 
						    	|| placeholder.Contains ("content") || placeholder.Contains ("Main"))
							primaryPlaceholderCombo.Active = i;
					}
				} catch (Exception ex) {
					LoggingService.LogError ("Unhandled exception getting master regions for '" + realPath + "'", ex);
				}
			}
			
			Validate ();
		}
		
		#region Public properties
		
		public IType ViewDataType {
			get {
				return (IType)dataClassCombo.CurrentItem;
			}
		}
		
		public string MasterFile {
			get {
				return masterEntry.Text;
			}
		}
		
		public bool HasMaster {
			get {
				return masterCheck.Active;
			}
		}
		
		public string PrimaryPlaceHolder {
			get {
				return primaryPlaceholderCombo.ActiveText;
			}
		}
		
		public List<string> ContentPlaceHolders {
			get; private set;
		}
		
		public string TemplateFile {
			get {
				return loadedTemplateList[templateCombo.Active];
			}
		}
		
		public string ViewName {
			get {
				return nameEntry.Text;
			}
			set {
				nameEntry.Text = value ?? "";
			}
		}
		
		public bool IsPartialView {
			get { return partialCheck.Active; }
		}
		
		public bool IsStronglyTyped {
			get { return stronglyTypedCheck.Active; }
		}
		
		#endregion
		
		class TypeDataProvider : DropDownBoxListWindow.IListDataProvider
		{
			Ambience ambience;
			
			public List<ITypeDefinition> List { get; private set; }
			
			public TypeDataProvider (MonoDevelop.Projects.DotNetProject project)
			{
				var ctx = TypeSystemService.GetCompilation (project);
				List = new List<ITypeDefinition> (ctx.MainAssembly.GetAllTypeDefinitions ());
				this.ambience = AmbienceService.GetAmbience (project.LanguageName);
			}
			
			public int IconCount { get { return List.Count; } }
			
			public void Reset ()
			{
				//called when the list is shown
			}
			
			public string GetMarkup (int n)
			{
				return ambience.GetString ((IEntity)List[n], OutputFlags.IncludeGenerics | OutputFlags.UseFullName | OutputFlags.IncludeMarkup);
			}
			
			public Gdk.Pixbuf GetIcon (int n)
			{
				return ImageService.GetPixbuf (List[n].GetStockIcon (),Gtk.IconSize.Menu);
			}
			
			public object GetTag (int n)
			{
				return List[n];
			}
			
			public void ActivateItem (int n)
			{
				// nothing
			}
		}
	}
}

		
