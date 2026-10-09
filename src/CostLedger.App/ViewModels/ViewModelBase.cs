using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CostLedger.App.ViewModels;

/// <summary>MVVM 基类（M1 不引入 ReactiveUI，保持轻量）</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
