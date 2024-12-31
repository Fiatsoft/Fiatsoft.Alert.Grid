using System.Windows;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media.Animation;

namespace Fiatsoft.Alert.Grid.Controls {
    public class SplitButton : ItemsControl, INotifyPropertyChanged {
        public SplitButton() {
            DefaultStyleKey = typeof(SplitButton);
        }

        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e) {
            base.OnItemsChanged(e);
            foreach (var item in this.Items) {
                if (item is Button button) {
                    if (userButtons.Contains(item)) 
                        continue;
                    button.Click += (s, e) => IsPopupOpen = false;
                    button.IsEnabledChanged += (s, e) => { IsDropdownButtonEnabled = userButtons.Any(e => e.IsEnabled); };
                    userButtons.Add(button as UIElement);
                }
            }
        }

        public override void OnApplyTemplate() {
            base.OnApplyTemplate();

            var popup = GetTemplateChild("PART_Popup") as Popup;
            var popupContent = GetTemplateChild("PopupContent") as Border;

            if (GetTemplateChild("PART_MainButton") is Button mainButton) {
                mainButton.Click += (s, e) => RaiseMainButtonClick();
            }

            if (GetTemplateChild("PART_DropdownButton") is Button dropdownButton) {
                dropdownButton.Click += (s, e) => {
                    if (IsPopupOpen)
                        return;
                    IsPopupOpen = true;
                };
            }

            if ((popup as dynamic)?.Child?.Child is ItemsPresenter itemsPresenter) {
                itemsPresenter.Loaded += (s, e) => {
                    int childCount = VisualTreeHelper.GetChildrenCount(itemsPresenter);
                    for (int i = 0; i < childCount; i++) {
                        var child = VisualTreeHelper.GetChild(itemsPresenter, i);
                        if (child is Button childButton) {
                            childButton.Click += (s, e) => {
                                IsPopupOpen = false;
                            };
                        }
                    }
                };
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged = delegate { };
        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public static readonly DependencyProperty MainButtonProperty =
            DependencyProperty.Register(
                nameof(MainButton),
                typeof(UIElement),
                typeof(SplitButton),
                new PropertyMetadata(null));

        public UIElement MainButton {
            get { return (UIElement)GetValue(MainButtonProperty); }
            set { SetValue(MainButtonProperty, value); }
        }

        private List<UIElement> userButtons = [];
        public List<UIElement> UserButtons {
            get { return userButtons; }
            set { userButtons = value; }
        }

        private bool _isPopupOpen;
        public bool IsPopupOpen {
            get { return _isPopupOpen; }
            set {
                if (_isPopupOpen != value) {
                    _isPopupOpen = value;
                    OnPopupStateChanged();
                }
            }
        }

        private bool isDropdownButtonEnabled = false;
        public bool IsDropdownButtonEnabled {
            get => isDropdownButtonEnabled;
            private set {
                if (isDropdownButtonEnabled != value) {
                    isDropdownButtonEnabled = value;
                    OnPropertyChanged(nameof(IsDropdownButtonEnabled));
                }
            }
        }

        public static readonly RoutedEvent MainButtonClickEvent = EventManager.RegisterRoutedEvent("MainButtonClick", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SplitButton));
        public event RoutedEventHandler MainButtonClick {
            add { AddHandler(MainButtonClickEvent, value); }
            remove { RemoveHandler(MainButtonClickEvent, value); }
        }
        internal void RaiseMainButtonClick() {
            RoutedEventArgs args = new(MainButtonClickEvent);
            RaiseEvent(args);
            IsPopupOpen = false;
        }

        private void OnPopupStateChanged() {
            if (GetTemplateChild("PART_Popup") is Popup popup && GetTemplateChild("PopupContent") is FrameworkElement popupContent) {
                if (IsPopupOpen) {
                    popup.IsOpen = true;
                    StartSlideDownAnimation(popupContent);
                }
                else {
                    popup.IsOpen = false;
                }
            }
        }

        private static void StartSlideDownAnimation(FrameworkElement popupContent) {
            if (popupContent.RenderTransform is not TranslateTransform transform) {
                transform = new TranslateTransform();
                popupContent.RenderTransform = transform;
            }

            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation {
                From = popupContent.ActualHeight * -1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

        }
    }
}
