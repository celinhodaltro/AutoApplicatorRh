namespace AutoApplicator.Domain.Enums;

public enum JobStatus
{
    New,
    Approved,
    Rejected,
    Pending,
    Applied,    // Job aplicado com sucesso
    Error       // Erro inesperado
}
