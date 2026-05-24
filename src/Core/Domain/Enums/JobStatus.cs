namespace AutoApplicator.Domain.Enums;

public enum JobStatus
{
    New,        // Job coletado, aguardando processamento
    Approved,   // Job aprovado para aplicar
    Rejected,   // Job rejeitado (não vai aplicar)
    Pending,    // Job aguardando respostas do usuário
    Applied,    // Job aplicado com sucesso
    Error       // Erro inesperado
}
