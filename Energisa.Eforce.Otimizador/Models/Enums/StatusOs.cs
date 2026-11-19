using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Energisa.Eforce.Otimizador.Models.Enums
{
    public enum StatusOs
    {
        // AguardandoEnvio = null,
        Despachado = 0,
        Reconhecido = 1,
        Lido = 2,
        Deslocamento = 3,
        Chegada = 8,
        Localizacao = 4,
        Execucao = 5,
        Finalizacao = 6,
        Encerramento = 7,
        Retirada = 9,
        Interrompida = 10,
        Redirecionada = 11,
        DespachoAutomatico = 12
    }
}
