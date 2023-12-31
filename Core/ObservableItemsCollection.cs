﻿using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using AeonHacs.Utilities;

namespace AeonHacs
{
    public class ObservableItemsCollection<T> : ObservableCollection<T> where T : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler ItemPropertyChanged;

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            // TODO: make this thread-safe, thread-aware, thread-functional, something
            try { base.OnCollectionChanged(e); } catch { }
            UnsubscribeFromItems(e.OldItems);
            SubscribeToItems(e.NewItems);
        }

        protected virtual void UnsubscribeFromItems(IList items)
        {
            if (items != null)
                foreach (INotifyPropertyChanged item in items)
                    item.PropertyChanged -= NotifyItemPropertyChanged;
        }

        protected virtual void SubscribeToItems(IList items)
        {
            if (items != null)
                foreach (INotifyPropertyChanged item in items)
                {
                    item.PropertyChanged += NotifyItemPropertyChanged;
                    NotifyItemPropertyChanged(item, null);
                }
        }

        protected virtual void NotifyItemPropertyChanged(object sender, PropertyChangedEventArgs e) =>
            ItemPropertyChanged?.Invoke(sender, e);

        public override string ToString()
        {
            return typeof(T).Name.Plural();
        }
    }
}