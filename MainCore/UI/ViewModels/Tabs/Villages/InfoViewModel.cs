using MainCore.Services;
using MainCore.Tasks;
using MainCore.UI.ViewModels.Abstract;
using MainCore.UI.Models.Output;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Text.Json;

namespace MainCore.UI.ViewModels.Tabs.Villages
{
    // 1. Transformamos o modelo em ReactiveObject para a edição na tabela funcionar em tempo real
    public class OasisModel : ReactiveObject
    {
        private int _x;
        public int X { get => _x; set => this.RaiseAndSetIfChanged(ref _x, value); }

        private int _y;
        public int Y { get => _y; set => this.RaiseAndSetIfChanged(ref _y, value); }

        private int _troopIndex;
        public int TroopIndex { get => _troopIndex; set => this.RaiseAndSetIfChanged(ref _troopIndex, value); }

        private int _minExp;
        public int MinExp { get => _minExp; set => this.RaiseAndSetIfChanged(ref _minExp, value); }

        private int _heroPower;
        public int HeroPower { get => _heroPower; set => this.RaiseAndSetIfChanged(ref _heroPower, value); }
    }

    [RegisterSingleton<InfoViewModel>]
    public class InfoViewModel : VillageTabViewModelBase
    {
        private readonly ITaskManager _taskManager;
        private readonly IDialogService _dialogService;
        private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "oasis_farm_list.json");

        public ObservableCollection<OasisModel> OasisList { get; } = new();

        private int _tribeIndex = 2; // Padrão Gauleses
        public int TribeIndex { get => _tribeIndex; set => this.RaiseAndSetIfChanged(ref _tribeIndex, value); }

        private int _oasisX;
        public int OasisX { get => _oasisX; set => this.RaiseAndSetIfChanged(ref _oasisX, value); }

        private int _oasisY;
        public int OasisY { get => _oasisY; set => this.RaiseAndSetIfChanged(ref _oasisY, value); }

        private int _troopIndexToUse = 2; // Padrão Espadachim
        public int TroopIndexToUse { get => _troopIndexToUse; set => this.RaiseAndSetIfChanged(ref _troopIndexToUse, value); }

        private int _minimumExpForHero = 20;
        public int MinimumExpForHero { get => _minimumExpForHero; set => this.RaiseAndSetIfChanged(ref _minimumExpForHero, value); }

        private int _heroAttackPower = 2000;
        public int HeroAttackPower { get => _heroAttackPower; set => this.RaiseAndSetIfChanged(ref _heroAttackPower, value); }

        public ReactiveCommand<Unit, Unit> AddOasisCommand { get; }
        public ReactiveCommand<Unit, Unit> PlayFarmCommand { get; }
        public ReactiveCommand<OasisModel, Unit> RemoveOasisCommand { get; }

        // 2. Novo comando para salvar a edição
        public ReactiveCommand<OasisModel, Unit> SaveOasisCommand { get; }

        public InfoViewModel(ITaskManager taskManager, IDialogService dialogService)
        {
            _taskManager = taskManager;
            _dialogService = dialogService;

            AddOasisCommand = ReactiveCommand.Create(AddOasisExecute);
            PlayFarmCommand = ReactiveCommand.CreateFromTask(PlayFarmExecute);
            RemoveOasisCommand = ReactiveCommand.Create<OasisModel>(RemoveOasisExecute);

            // Registra o comando de salvar
            SaveOasisCommand = ReactiveCommand.CreateFromTask<OasisModel>(SaveOasisExecute);

            LoadData();
        }

        protected override Task Load(VillageId villageId)
        {
            return Task.CompletedTask;
        }

        private void AddOasisExecute()
        {
            OasisList.Add(new OasisModel
            {
                X = OasisX,
                Y = OasisY,
                TroopIndex = TroopIndexToUse,
                MinExp = MinimumExpForHero,
                HeroPower = HeroAttackPower
            });
            SaveData();
        }

        private void RemoveOasisExecute(OasisModel oasis)
        {
            if (oasis != null)
            {
                OasisList.Remove(oasis);
                SaveData();
            }
        }

        // 3. A lógica do botão de Salvar Edição
        private async Task SaveOasisExecute(OasisModel oasis)
        {
            if (oasis != null)
            {
                SaveData(); // Reescreve o JSON com os novos valores editados na interface
                await _dialogService.MessageBox.Handle(new MessageBoxData("Salvo", $"As alterações no Oásis ({oasis.X}|{oasis.Y}) foram salvas com sucesso no arquivo."));
            }
        }

        private void SaveData()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(OasisList, options);
                File.WriteAllText(_filePath, json);
            }
            catch { /* Ignorar erros de IO silenciosamente */ }
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var data = JsonSerializer.Deserialize<ObservableCollection<OasisModel>>(json);
                    if (data != null)
                    {
                        OasisList.Clear();
                        foreach (var item in data) OasisList.Add(item);
                    }
                }
            }
            catch { /* Arquivo corrompido ou vazio */ }
        }

        private async Task PlayFarmExecute()
        {
            if (AccountId == AccountId.Empty || VillageId == VillageId.Empty)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Erro", "Nenhuma aldeia detectada!"));
                return;
            }

            if (OasisList.Count == 0)
            {
                await _dialogService.MessageBox.Handle(new MessageBoxData("Aviso", "A lista de oásis está vazia!"));
                return;
            }

            int tribeEnumValue = TribeIndex + 1;

            var listOfOasis = OasisList.Select(o => new MainCore.Tasks.AttackOasisTask.OasisTarget
            {
                X = o.X,
                Y = o.Y,
                TroopIndexToUse = o.TroopIndex,
                MinimumExpForHero = o.MinExp,
                HeroAttackPower = o.HeroPower
            }).ToList();

            var task = new MainCore.Tasks.AttackOasisTask.Task(
                AccountId,
                VillageId,
                listOfOasis,
                tribeEnumValue)
            {
                ExecuteAt = DateTime.Now
            };

            _taskManager.Add(task);

            await _dialogService.MessageBox.Handle(new MessageBoxData("Sucesso! 🚀", $"Varredura e Ataques de {OasisList.Count} oásis enviados para a fila com sucesso!"));
        }
    }
}
