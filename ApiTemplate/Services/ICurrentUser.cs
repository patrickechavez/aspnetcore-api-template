namespace ApiTemplate.Services;

public interface ICurrentUser
{
    Guid? Id { get; }

    bool IsAdmin { get; }

    bool IsAuthenticated { get; }
}
