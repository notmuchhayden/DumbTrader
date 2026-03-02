using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DumbTrader.Core
{
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Predicate<object?>? _canExecute;
        
        // 실행 중복 방지를 위한 플래그
        private bool _isExecuting;

        public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        // 실행 중(_isExecuting)이면 false를 반환하여 버튼 비활성화 기능 지원
        public bool CanExecute(object? parameter) 
        {
            if (_isExecuting) return false;
            return _canExecute == null || _canExecute(parameter);
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged(); // 상태 변경 알림 (버튼 비활성화)

                await _execute(parameter); // 비동기 작업 대기
            }
            catch (Exception ex)
            {
                // TODO: 여기서 전역 예외 또는 로깅 처리
                Console.WriteLine($"Async Command Error: {ex.Message}");
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged(); // 상태 변경 알림 (버튼 활성화)
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
