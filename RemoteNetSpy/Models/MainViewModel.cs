using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;


namespace RemoteNetSpy.Models
{
    using System.Collections.ObjectModel;
    using System.ComponentModel;

    public class ProductModel : INotifyPropertyChanged
    {
        private string _name;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class StoreModel : INotifyPropertyChanged
    {
        private string _storeName;
        private ObservableCollection<ProductModel> _products;

        public string StoreName
        {
            get => _storeName;
            set
            {
                _storeName = value;
                OnPropertyChanged(nameof(StoreName));
            }
        }

        public ObservableCollection<ProductModel> Products
        {
            get => _products;
            set
            {
                _products = value;
                OnPropertyChanged(nameof(Products));
            }
        }

        public StoreModel()
        {
            Products = new ObservableCollection<ProductModel>();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
