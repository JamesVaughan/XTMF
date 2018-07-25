﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using XTMF.Gui.Interfaces;
using XTMF.Gui.Models;

namespace XTMF.Gui.UserControls
{
    /// <summary>
    /// Interaction logic for ModelSystemTreeViewDisplay.xaml
    /// </summary>
    public partial class ModelSystemTreeViewDisplay : UserControl, IModelSystemView
    {

        private ModelSystemDisplay display;

        public ModelSystemStructureDisplayModel SelectedModule => ModuleDisplay.SelectedItem as ModelSystemStructureDisplayModel;

        public ItemsControl ViewItemsControl => ModuleDisplay;

        private bool _disableMultipleSelectOnce;

        private static readonly PropertyInfo IsSelectionChangeActiveProperty = typeof(TreeView).GetProperty(
    "IsSelectionChangeActive", BindingFlags.NonPublic | BindingFlags.Instance);

        internal List<ModelSystemStructureDisplayModel> CurrentlySelected
        {
            get
            {
                return display.CurrentlySelected;
            }
        }

        public ModelSystemTreeViewDisplay(ModelSystemDisplay display)
        {
            InitializeComponent();
            this.display = display;
            //ModuleDisplay.SelectedItemChanged += ModuleDisplay_SelectedItemChanged;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ModelSystemDisplay_Loaded(object sender, RoutedEventArgs e)
        {
            // This needs to be executed via the dispatcher to avoid an issue with AvalonDock

            this.display.UpdateQuickParameters();
            this.display.EnumerateDisabled(ModuleDisplay.Items.GetItemAt(0) as ModelSystemStructureDisplayModel);
            this.display.ModuleContextControl.ModuleContextChanged += ModuleContextControlOnModuleContextChanged;
        }



        private void MDisplay_Unloaded(object sender, RoutedEventArgs e)
        {
            MainWindow.Us.PreviewKeyDown -= UsOnPreviewKeyDown;
        }

        /// <summary>
        ///     Callback for when the Module Context control changes the active "selected module
        /// </summary>
        /// <param name="sender1"></param>
        /// <param name="eventArgs"></param>
        private void ModuleContextControlOnModuleContextChanged(object sender1, ModuleContextChangedEventArgs eventArgs)
        {
            Dispatcher.Invoke(() =>
            {
                if (eventArgs.Module != null)
                {
                    ExpandToRoot(eventArgs.Module);
                    eventArgs.Module.IsSelected = true;
                    ModuleDisplay.Focus();
                    Keyboard.Focus(ModuleDisplay);
                }
            });
        }

        /// <summary>
        /// Expands a module, tracing backwards until the root module is reached
        /// </summary>
        /// <param name="module"></param>
        public void ExpandToRoot(ModelSystemStructureDisplayModel module)
        {
            // don't expand the bottom node
            module = module?.Parent;
            while (module != null)
            {
                module.IsExpanded = true;
                module = module.Parent;
            }
        }

        /// <summary>
        /// Expand all menu item click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ExpandAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ModuleDisplay.SelectedItem != null)
            {
                if (ModuleDisplay.Items.Count > 0)
                {
                    ExpandModule((ModelSystemStructureDisplayModel)ModuleDisplay.SelectedItem);
                }
            }
        }

        /// <summary>
        /// Collapse all menu item click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CollapseAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ModuleDisplay.SelectedItem != null)
            {
                if (ModuleDisplay.Items.Count > 0)
                {
                    ExpandModule((ModelSystemStructureDisplayModel)ModuleDisplay.SelectedItem, false);
                }
            }
        }

        /// <summary>
        /// Expands or collapses a module and its children.
        /// </summary>
        /// <param name="module"></param>
        /// <param name="collapse"></param>
        private void ExpandModule(ModelSystemStructureDisplayModel module, bool collapse = true)
        {
            if (module != null)
            {
                var toProcess = new Queue<ModelSystemStructureDisplayModel>();
                toProcess.Enqueue(module);
                while (toProcess.Count > 0)
                {
                    module = toProcess.Dequeue();
                    module.IsExpanded = collapse;
                    foreach (var child in module.Children)
                    {
                        toProcess.Enqueue(child);
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="up"></param>
        private void MoveFocusNextModule(bool up)
        {
            Keyboard.Focus(ModuleDisplay);
            MoveFocusNext(up);
        }

        private void ToggleDisableModule()
        {
            var selected = (ModuleDisplay.SelectedItem as ModelSystemStructureDisplayModel)?.BaseModel;
            var selectedModuleControl = GetCurrentlySelectedControl();
            if (selectedModuleControl != null && selected != null)
            {
                string error = null;
                Session.ExecuteCombinedCommands(selected.IsDisabled ? "Enable Module" : "Disable Module", () =>
                {
                    foreach (var sel in CurrentlySelected)
                    {
                        if (!sel.SetDisabled(!sel.IsDisabled, ref error))
                        {
                            return;
                        }

                        if (sel.IsDisabled)
                        {
                            if (!DisabledModules.Contains(sel))
                            {
                                DisabledModules.Add(sel);
                            }
                        }
                    }
                });
                if (error != null)
                {
                    MessageBox.Show(MainWindow.Us, error,
                        selected.IsDisabled ? "Unable to Enable" : "Unable to Disable", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ModuleDisplay_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var item = ModuleDisplay.SelectedItem as ModelSystemStructureDisplayModel;
            e.Handled = false;
            switch (e.Key)
            {
                case Key.F2:
                    this.display.RenameSelectedModule();
                    break;
                case Key.Up:
                    ModuleDisplayNavigateUp(item);
                    e.Handled = true;
                    break;
                case Key.Down:
                    ModuleDisplayNavigateDown(item);
                    e.Handled = true;
                    break;
            }
        }

        private void ModuleDisplayNavigateDown(ModelSystemStructureDisplayModel item)
        {
            if (item.IsExpanded && item.Children != null && item.Children.Count > 0)
            {
                item.Children[0].IsSelected = true;
            }
            else
            {
                var toSelect = FindNextAncestor(item);
                if (item.Parent == toSelect.Parent && item.Index < item.Parent.Children.Count - 1
                    || item.Parent != toSelect.Parent)
                {
                    toSelect.IsSelected = true;
                }
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            MoveCurrentModule(-1);
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            MoveCurrentModule(1);
        }

        private void MoveCurrentModule(int deltaPosition)
        {
            if (CurrentlySelected.Count > 0)
            {
                var parent = Session.GetParent(CurrentlySelected[0].BaseModel);
                // make sure they all have the same parent
                if (CurrentlySelected.Any(m => Session.GetParent(m.BaseModel) != parent))
                {
                    // if not ding and exit
                    SystemSounds.Asterisk.Play();
                    return;
                }

                var mul = deltaPosition < 0 ? 1 : -1;
                var moveOrder = CurrentlySelected
                    .Select((c, i) => new { Index = i, ParentIndex = parent.Children.IndexOf(c.BaseModel) })
                    .OrderBy(i => mul * i.ParentIndex);
                var first = moveOrder.First();
                Session.ExecuteCombinedCommands(
                    "Move Selected Modules",
                    () =>
                    {
                        foreach (var el in moveOrder)
                        {
                            var selected = CurrentlySelected[el.Index];
                            string error = null;
                            if (!selected.BaseModel.MoveModeInParent(deltaPosition, ref error))
                            {
                                SystemSounds.Asterisk.Play();
                                break;
                            }
                        }
                    });
                BringSelectedIntoView(CurrentlySelected[first.Index]);
            }
        }


        private void SetMetaModuleStateForSelected(bool set)
        {
            Session.ExecuteCombinedCommands(
                set ? "Compose to Meta-Modules" : "Decompose Meta-Modules",
                () =>
                {
                    foreach (var selected in CurrentlySelected)
                    {
                        string error = null;
                        if (!selected.SetMetaModule(set, ref error))
                        {
                            MessageBox.Show(GetWindow(), error, "Failed to convert meta module.", MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                    }
                });
            UpdateParameters();
        }




        private void ConvertToMetaModule_Click(object sender, RoutedEventArgs e)
        {
            SetMetaModuleStateForSelected(true);
        }

        private void ConvertFromMetaModule_Click(object sender, RoutedEventArgs e)
        {
            SetMetaModuleStateForSelected(false);
        }





        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GridCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ModuleDisplay.Focus();
        }

        /// <summary>
        /// </summary>
        /// <param name="treeView"></param>
        /// <see cref="http://stackoverflow.com/questions/1163801/wpf-treeview-with-multiple-selection" />
        public void AllowMultiSelection(TreeView treeView)
        {
            if (IsSelectionChangeActiveProperty == null)
            {
                return;
            }

            var selectedItems = new List<TreeViewItem>();
            treeView.SelectedItemChanged += (a, b) =>
            {
                var module = GetCurrentlySelectedControl();
                if (module == null)
                {
                    // disable the event to avoid recursion
                    var isSelectionChangeActive = IsSelectionChangeActiveProperty.GetValue(treeView, null);
                    IsSelectionChangeActiveProperty.SetValue(treeView, true, null);
                    selectedItems.ForEach(item => item.IsSelected = true);
                    // enable the event to avoid recursion
                    IsSelectionChangeActiveProperty.SetValue(treeView, isSelectionChangeActive, null);
                    return;
                }

                var treeViewItem = VisualUpwardSearch(module);
                if (treeViewItem == null)
                {
                    return;
                }

                var disableMultiple = _disableMultipleSelectOnce;
                _disableMultipleSelectOnce = false;
                var currentItem = treeView.SelectedItem as ModelSystemStructureDisplayModel;
                // allow multiple selection
                // when control key is pressed
                if (!disableMultiple && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
                {
                    // suppress selection change notification
                    // select all selected items
                    // then restore selection change notifications
                    var isSelectionChangeActive = IsSelectionChangeActiveProperty.GetValue(treeView, null);
                    IsSelectionChangeActiveProperty.SetValue(treeView, true, null);
                    selectedItems.ForEach(item => item.IsSelected =
                        item != treeViewItem || !selectedItems.Contains(treeViewItem));
                    IsSelectionChangeActiveProperty.SetValue(treeView, isSelectionChangeActive, null);
                }
                else if ((Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) &&
                         CurrentlySelected.Count > 0)
                {
                    var isSelectionChangeActive = IsSelectionChangeActiveProperty.GetValue(treeView, null);
                    IsSelectionChangeActiveProperty.SetValue(treeView, true, null);
                    // select the range
                    var lastSelected = CurrentlySelected.Last();
                    var lastTreeItem = selectedItems.Last();
                    var currentParent = VisualUpwardSearch(VisualTreeHelper.GetParent(treeViewItem));
                    var lastParent = VisualUpwardSearch(VisualTreeHelper.GetParent(lastTreeItem));
                    if (currentParent != null && currentParent == lastParent)
                    {
                        var itemGenerator = currentParent.ItemContainerGenerator;
                        var lastSelectedIndex = itemGenerator.IndexFromContainer(lastTreeItem);
                        var currentSelectedIndex = itemGenerator.IndexFromContainer(treeViewItem);
                        var minIndex = Math.Min(lastSelectedIndex, currentSelectedIndex);
                        var maxIndex = Math.Max(lastSelectedIndex, currentSelectedIndex);
                        for (var i = minIndex; i <= maxIndex; i++)
                        {
                            var innerTreeViewItem = itemGenerator.ContainerFromIndex(i) as TreeViewItem;
                            var innerModule = itemGenerator.Items[i] as ModelSystemStructureDisplayModel;
                            if (CurrentlySelected.Contains(innerModule))
                            {
                                CurrentlySelected.Remove(innerModule);
                            }

                            CurrentlySelected.Add(innerModule);
                            selectedItems.Add(innerTreeViewItem);
                        }
                    }

                    // select all of the modules that should be selected
                    selectedItems.ForEach(item => item.IsSelected = true);
                    IsSelectionChangeActiveProperty.SetValue(treeView, isSelectionChangeActive, null);
                    return;
                }
                else
                {
                    // deselect all selected items (current one will be re-added)
                    CurrentlySelected.Clear();
                    selectedItems.ForEach(item => item.IsSelected = item == treeViewItem);
                    selectedItems.Clear();
                }

                if (!selectedItems.Contains(treeViewItem))
                {
                    selectedItems.Add(treeViewItem);
                    CurrentlySelected.Add(currentItem);
                }
                else
                {
                    // deselect if already selected
                    CurrentlySelected.Remove(currentItem);
                    treeViewItem.IsSelected = false;
                    selectedItems.Remove(treeViewItem);
                }
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        private static TreeViewItem VisualUpwardSearch(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            return source as TreeViewItem;
        }




    }
}
