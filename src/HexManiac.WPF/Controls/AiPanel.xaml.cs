using HavenSoft.HexManiac.Core.ViewModels.AI;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HavenSoft.HexManiac.WPF.Controls {
   public partial class AiPanel {
      public AiPanel() {
         InitializeComponent();
         Loaded += (s, e) => {
            if (DataContext is AiToolViewModel vm) {
               vm.Messages.CollectionChanged += Messages_CollectionChanged;
               // Initialize PasswordBox with existing key
               if (!string.IsNullOrEmpty(vm.Settings.ApiKey)) {
                  ApiKeyBox.Password = vm.Settings.ApiKey;
               }
            }
         };
      }

      private void Messages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
         MessageScroller.ScrollToEnd();
      }

      private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e) {
         if (DataContext is AiToolViewModel vm) {
            vm.Settings.ApiKey = ApiKeyBox.Password;
         }
      }

      private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e) {
         if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) {
            e.Handled = true;
            if (DataContext is AiToolViewModel vm && vm.SendCommand.CanExecute(null)) {
               vm.SendCommand.Execute(null);
            }
         } else if (e.Key == Key.Escape) {
            e.Handled = true;
            if (DataContext is AiToolViewModel vm) {
               if (vm.IsProcessing) {
                  vm.CancelCommand.Execute(null);
               } else {
                  vm.Close();
               }
            }
         }
      }
   }
}
